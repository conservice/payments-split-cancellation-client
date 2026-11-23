using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Conservice.Payments.SplitCancellation.Client.Classification;
using Conservice.Payments.SplitCancellation.Client.Contracts;
using Microsoft.Extensions.Logging;

namespace Conservice.Payments.SplitCancellation.Client;

/// <inheritdoc cref="IGatekeeperCancelClient"/>
internal sealed class GatekeeperCancelClient : IGatekeeperCancelClient
{
    internal const string CancelBatchPath = "tasks/cancel-batch";
    private const int MaxBodyLengthInMessage = 500;

    // Web defaults match ASP.NET Core: camelCase out, case-insensitive in. The enum converter
    // still reads numbers (what Gatekeeper sends) and also accepts names.
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _httpClient;
    private readonly IGatekeeperAccessTokenProvider _tokenProvider;
    private readonly ILogger<GatekeeperCancelClient> _logger;

    public GatekeeperCancelClient(
        HttpClient httpClient,
        IGatekeeperAccessTokenProvider tokenProvider,
        ILogger<GatekeeperCancelClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SplitCancelDecision> CancelSplitAsync(
        Guid splitCorrelation,
        CancellationToken cancellationToken = default)
    {
        var result = await CancelSplitsAsync([splitCorrelation], cancellationToken).ConfigureAwait(false);
        return result.Decisions[0];
    }

    public async Task<CancelSplitsResult> CancelSplitsAsync(
        IEnumerable<Guid> splitCorrelations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(splitCorrelations);
        var requested = splitCorrelations.Distinct().ToArray();
        if (requested.Length == 0)
        {
            throw new ArgumentException("At least one split correlation is required.", nameof(splitCorrelations));
        }

        string? token;
        try
        {
            token = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not get an access token for Gatekeeper; every split classifies as Retry.{Data.SplitCount}",
                requested.Length);
            return CancelSplitsResult.ForAll(requested, CancelClassification.Retry, null,
                $"Could not get an access token for Gatekeeper: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogError("No access token for Gatekeeper; every split classifies as Retry.{Data.SplitCount}",
                requested.Length);
            return CancelSplitsResult.ForAll(requested, CancelClassification.Retry, null,
                "No access token for Gatekeeper.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, CancelBatchPath)
        {
            Content = JsonContent.Create(new CancelBatchRequest(requested), options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Includes HttpClient.Timeout, which surfaces as TaskCanceledException.
            _logger.LogWarning(ex, "Cancel request to Gatekeeper failed; every split classifies as Retry.{Data.SplitCount}",
                requested.Length);
            return CancelSplitsResult.ForAll(requested, CancelClassification.Retry, null,
                $"Cancel request to Gatekeeper failed: {ex.Message}");
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (response.StatusCode != HttpStatusCode.OK)
            {
                var body = await ReadBodyForMessageAsync(response, cancellationToken).ConfigureAwait(false);
                var classification = CancelOutcomeClassifier.ClassifyBatchFailureStatus(response.StatusCode);
                _logger.Log(
                    classification == CancelClassification.Terminal ? LogLevel.Error : LogLevel.Warning,
                    "Gatekeeper answered the cancel request with a non-200 status.{Data.HttpStatus}{Data.Classification}{Data.SplitCount}{Data.Body}",
                    status, classification, requested.Length, body);
                return CancelSplitsResult.ForAll(requested, classification, status,
                    $"Gatekeeper answered HTTP {status}: {body}");
            }

            CancelBatchResponse? payload;
            try
            {
                payload = await response.Content
                    .ReadFromJsonAsync<CancelBatchResponse>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not read Gatekeeper's cancel answer; every split classifies as Retry.{Data.SplitCount}",
                    requested.Length);
                return CancelSplitsResult.ForAll(requested, CancelClassification.Retry, status,
                    $"Could not read Gatekeeper's cancel answer: {ex.Message}");
            }

            return new CancelSplitsResult(Classify(requested, payload?.Results), status);
        }
    }

    private SplitCancelDecision[] Classify(
        IReadOnlyList<Guid> requested,
        IReadOnlyList<SplitCancellationResult>? results)
    {
        var bySplit = new Dictionary<Guid, SplitCancellationResult>();
        foreach (var result in results ?? [])
        {
            if (result is not null)
            {
                bySplit.TryAdd(result.SplitCorrelationId, result);
            }
        }

        var decisions = new SplitCancelDecision[requested.Count];
        for (var i = 0; i < requested.Count; i++)
        {
            var splitCorrelation = requested[i];
            if (!bySplit.TryGetValue(splitCorrelation, out var result))
            {
                _logger.LogWarning("Gatekeeper returned no result for a split; it classifies as Retry.{Data.SplitCorrelation}",
                    splitCorrelation);
                decisions[i] = new SplitCancelDecision(splitCorrelation, CancelClassification.Retry, null,
                    "Gatekeeper returned no result for this split.");
                continue;
            }

            if (!CancelOutcomeClassifier.TryClassifyKnown(result.Outcome, out var classification))
            {
                classification = CancelClassification.Retry;
                _logger.LogWarning(
                    "Gatekeeper answered an outcome this client does not know; it classifies as Retry. Update the client package.{Data.SplitCorrelation}{Data.Outcome}{Data.Message}",
                    splitCorrelation, (int)result.Outcome, result.Message);
            }
            else if (classification == CancelClassification.Terminal)
            {
                _logger.LogError(
                    "Gatekeeper cancel is terminal: it will never be confirmed. Reconcile the split with its execution outcome.{Data.SplitCorrelation}{Data.Outcome}{Data.Message}",
                    splitCorrelation, result.Outcome, result.Message);
            }

            decisions[i] = new SplitCancelDecision(splitCorrelation, classification, result.Outcome, result.Message ?? string.Empty);
        }

        return decisions;
    }

    private static async Task<string> ReadBodyForMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return body.Length <= MaxBodyLengthInMessage ? body : body[..MaxBodyLengthInMessage] + "…";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
