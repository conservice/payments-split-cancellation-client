using System.Net;
using System.Text.Json;
using Conservice.Payments.SplitCancellation.Client.Classification;
using Conservice.Payments.SplitCancellation.Client.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Conservice.Payments.SplitCancellation.Client.Tests;

[TestFixture]
public class GatekeeperCancelClientShould
{
    private static readonly Uri BaseAddress = new("https://gatekeeper.test/api/v1/");

    private static GatekeeperCancelClient CreateClient(
        StubHttpMessageHandler handler,
        IGatekeeperAccessTokenProvider? tokenProvider = null) =>
        new(
            new HttpClient(handler) { BaseAddress = BaseAddress },
            tokenProvider ?? StubTokenProvider.Returning("token-123"),
            NullLogger<GatekeeperCancelClient>.Instance);

    private static string BatchAnswer(params (Guid Split, int Outcome, string Message)[] results) =>
        JsonSerializer.Serialize(new
        {
            results = results.Select(r => new { splitCorrelationId = r.Split, outcome = r.Outcome, message = r.Message }),
        });

    [Test]
    public async Task PostToCancelBatchWithBearerTokenAndCamelCaseBody()
    {
        var split = Guid.NewGuid();
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, BatchAnswer((split, 0, "ok")));

        await CreateClient(handler).CancelSplitAsync(split);

        Assert.Multiple(() =>
        {
            Assert.That(handler.LastRequest!.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(handler.LastRequest.RequestUri, Is.EqualTo(new Uri("https://gatekeeper.test/api/v1/tasks/cancel-batch")));
            Assert.That(handler.LastRequest.Headers.Authorization?.Scheme, Is.EqualTo("Bearer"));
            Assert.That(handler.LastRequest.Headers.Authorization?.Parameter, Is.EqualTo("token-123"));
            Assert.That(handler.LastRequestBody, Is.EqualTo($"{{\"splitCorrelations\":[\"{split}\"]}}"));
        });
    }

    [Test]
    public async Task ClassifyEachSplitFromNumericOutcomesInRequestOrder()
    {
        var cancelled = Guid.NewGuid();
        var registered = Guid.NewGuid();
        var completed = Guid.NewGuid();
        var executing = Guid.NewGuid();
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, BatchAnswer(
            (executing, 3, "Task is already in execution."),
            (completed, 7, "Task already completed."),
            (registered, 8, "Cancellation recorded."),
            (cancelled, 0, "Task canceled.")));

        var result = await CreateClient(handler).CancelSplitsAsync([cancelled, registered, completed, executing]);

        Assert.Multiple(() =>
        {
            Assert.That(result.HttpStatusCode, Is.EqualTo(200));
            Assert.That(result.Decisions.Select(d => d.SplitCorrelationId),
                Is.EqualTo(new[] { cancelled, registered, completed, executing }));
            Assert.That(result.Decisions.Select(d => d.Classification), Is.EqualTo(new[]
            {
                CancelClassification.Confirmed,
                CancelClassification.Confirmed,
                CancelClassification.Terminal,
                CancelClassification.Retry,
            }));
            Assert.That(result.Decisions[2].Outcome, Is.EqualTo(SplitCancellationOutcome.Completed));
            Assert.That(result.Decisions[2].Message, Is.EqualTo("Task already completed."));
            Assert.That(result.Confirmed.Select(d => d.SplitCorrelationId), Is.EqualTo(new[] { cancelled, registered }));
            Assert.That(result.Terminal.Select(d => d.SplitCorrelationId), Is.EqualTo(new[] { completed }));
            Assert.That(result.Retry.Select(d => d.SplitCorrelationId), Is.EqualTo(new[] { executing }));
            Assert.That(result.AllConfirmed, Is.False);
        });
    }

    [Test]
    public async Task AcceptOutcomeNames()
    {
        var split = Guid.NewGuid();
        var body = $"{{\"results\":[{{\"splitCorrelationId\":\"{split}\",\"outcome\":\"CancelRegistered\",\"message\":\"\"}}]}}";
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, body);

        var decision = await CreateClient(handler).CancelSplitAsync(split);

        Assert.That(decision.Classification, Is.EqualTo(CancelClassification.Confirmed));
    }

    [Test]
    public async Task ClassifyAnUnknownOutcomeNumberAsRetry()
    {
        var split = Guid.NewGuid();
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, BatchAnswer((split, 42, "new answer")));

        var decision = await CreateClient(handler).CancelSplitAsync(split);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Classification, Is.EqualTo(CancelClassification.Retry));
            Assert.That((int?)decision.Outcome, Is.EqualTo(42));
        });
    }

    [Test]
    public async Task ClassifyASplitMissingFromTheAnswerAsRetry()
    {
        var answered = Guid.NewGuid();
        var missing = Guid.NewGuid();
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, BatchAnswer((answered, 0, "ok"), (Guid.NewGuid(), 0, "not asked")));

        var result = await CreateClient(handler).CancelSplitsAsync([answered, missing]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Decisions, Has.Count.EqualTo(2));
            Assert.That(result.Decisions[0].Classification, Is.EqualTo(CancelClassification.Confirmed));
            Assert.That(result.Decisions[1].Classification, Is.EqualTo(CancelClassification.Retry));
            Assert.That(result.Decisions[1].Outcome, Is.Null);
        });
    }

    [TestCase("{}")]
    [TestCase("{\"results\":null}")]
    public async Task ClassifyAnAnswerWithoutResultsAsRetry(string body)
    {
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, body);

        var decision = await CreateClient(handler).CancelSplitAsync(Guid.NewGuid());

        Assert.That(decision.Classification, Is.EqualTo(CancelClassification.Retry));
    }

    [TestCase("not json")]
    [TestCase("{\"results\":[{\"splitCorrelationId\":\"x\"}]}")]
    [TestCase("{\"results\":[{\"splitCorrelationId\":\"00000000-0000-0000-0000-000000000001\",\"outcome\":\"SomethingNew\"}]}")]
    public async Task ClassifyAnUnreadableAnswerAsRetry(string body)
    {
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, body);

        var result = await CreateClient(handler).CancelSplitsAsync([Guid.NewGuid(), Guid.NewGuid()]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Decisions.All(d => d.IsRetry), Is.True);
            Assert.That(result.HttpStatusCode, Is.EqualTo(200));
        });
    }

    [TestCase(HttpStatusCode.InternalServerError, CancelClassification.Retry)]
    [TestCase(HttpStatusCode.ServiceUnavailable, CancelClassification.Retry)]
    [TestCase(HttpStatusCode.Unauthorized, CancelClassification.Retry)]
    [TestCase(HttpStatusCode.NotFound, CancelClassification.Retry)]
    [TestCase(HttpStatusCode.BadRequest, CancelClassification.Terminal)]
    public async Task ClassifyEverySplitFromANonOkStatus(HttpStatusCode status, CancelClassification expected)
    {
        var handler = StubHttpMessageHandler.Answering(status, "{\"title\":\"problem\"}");

        var result = await CreateClient(handler).CancelSplitsAsync([Guid.NewGuid(), Guid.NewGuid()]);

        Assert.Multiple(() =>
        {
            Assert.That(result.HttpStatusCode, Is.EqualTo((int)status));
            Assert.That(result.Decisions.Select(d => d.Classification), Is.All.EqualTo(expected));
            Assert.That(result.Decisions.Select(d => d.Outcome), Is.All.Null);
            Assert.That(result.Decisions[0].Message, Does.Contain(((int)status).ToString()).And.Contain("problem"));
        });
    }

    [Test]
    public async Task ShortenALongErrorBodyInTheMessage()
    {
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.InternalServerError, new string('x', 5000));

        var decision = await CreateClient(handler).CancelSplitAsync(Guid.NewGuid());

        Assert.That(decision.Message.Length, Is.LessThan(600));
    }

    [Test]
    public async Task ClassifyANetworkFailureAsRetry()
    {
        var handler = StubHttpMessageHandler.Throwing(new HttpRequestException("connection refused"));

        var decision = await CreateClient(handler).CancelSplitAsync(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(decision.Classification, Is.EqualTo(CancelClassification.Retry));
            Assert.That(decision.Message, Does.Contain("connection refused"));
        });
    }

    [Test]
    public async Task ClassifyATimeoutAsRetry()
    {
        var handler = new StubHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new GatekeeperCancelClient(
            new HttpClient(handler) { BaseAddress = BaseAddress, Timeout = TimeSpan.FromMilliseconds(50) },
            StubTokenProvider.Returning("token"),
            NullLogger<GatekeeperCancelClient>.Instance);

        var decision = await client.CancelSplitAsync(Guid.NewGuid());

        Assert.That(decision.Classification, Is.EqualTo(CancelClassification.Retry));
    }

    [Test]
    public void ThrowWhenTheCallerCancels()
    {
        using var cts = new CancellationTokenSource();
        var handler = new StubHttpMessageHandler(async (_, ct) =>
        {
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        Assert.That(async () => await CreateClient(handler).CancelSplitAsync(Guid.NewGuid(), cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public async Task ClassifyAMissingTokenAsRetryWithoutCallingGatekeeper(string? token)
    {
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, "{}");

        var decision = await CreateClient(handler, StubTokenProvider.Returning(token)).CancelSplitAsync(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(decision.Classification, Is.EqualTo(CancelClassification.Retry));
            Assert.That(handler.CallCount, Is.Zero);
        });
    }

    [Test]
    public async Task ClassifyATokenFailureAsRetryWithoutCallingGatekeeper()
    {
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, "{}");
        var tokenProvider = new StubTokenProvider(_ => throw new InvalidOperationException("keycloak down"));

        var decision = await CreateClient(handler, tokenProvider).CancelSplitAsync(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(decision.Classification, Is.EqualTo(CancelClassification.Retry));
            Assert.That(decision.Message, Does.Contain("keycloak down"));
            Assert.That(handler.CallCount, Is.Zero);
        });
    }

    [Test]
    public async Task SendDuplicateSplitsOnce()
    {
        var split = Guid.NewGuid();
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, BatchAnswer((split, 0, "ok")));

        var result = await CreateClient(handler).CancelSplitsAsync([split, split]);

        Assert.Multiple(() =>
        {
            Assert.That(handler.LastRequestBody, Is.EqualTo($"{{\"splitCorrelations\":[\"{split}\"]}}"));
            Assert.That(result.Decisions, Has.Count.EqualTo(1));
            Assert.That(result.AllConfirmed, Is.True);
        });
    }

    private static Guid[] SplitsIn(string? body) =>
        JsonSerializer.Deserialize<CancelBatchRequest>(body!, GatekeeperCancelClient.JsonOptions)!
            .SplitCorrelations.ToArray();

    // Behaves like Gatekeeper: 400 above 100 splits, otherwise the given outcome for every split.
    private static StubHttpMessageHandler GatekeeperLike(Func<Guid[], HttpStatusCode>? statusFor = null) =>
        new(async (request, ct) =>
        {
            var splits = SplitsIn(await request.Content!.ReadAsStringAsync(ct));
            var status = splits.Length > GatekeeperCancelClient.MaxSplitsPerRequest
                ? HttpStatusCode.BadRequest
                : statusFor?.Invoke(splits) ?? HttpStatusCode.OK;
            var body = status == HttpStatusCode.OK
                ? BatchAnswer(splits.Select(s => (s, 0, "ok")).ToArray())
                : "{\"title\":\"problem\"}";
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        });

    [Test]
    public async Task SendOneHundredSplitsInOneCall()
    {
        var splits = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToArray();
        var handler = GatekeeperLike();

        var result = await CreateClient(handler).CancelSplitsAsync(splits);

        Assert.Multiple(() =>
        {
            Assert.That(handler.CallCount, Is.EqualTo(1));
            Assert.That(result.AllConfirmed, Is.True);
        });
    }

    [Test]
    public async Task SendMoreThanOneHundredSplitsInChunksAndKeepRequestOrder()
    {
        var splits = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray();
        var handler = GatekeeperLike();

        var result = await CreateClient(handler).CancelSplitsAsync(splits);

        Assert.Multiple(() =>
        {
            Assert.That(handler.CallCount, Is.EqualTo(2));
            Assert.That(handler.RequestBodies.Select(b => SplitsIn(b).Length), Is.EqualTo(new[] { 100, 1 }));
            Assert.That(result.Decisions.Select(d => d.SplitCorrelationId), Is.EqualTo(splits));
            Assert.That(result.AllConfirmed, Is.True);
            Assert.That(result.HttpStatusCode, Is.EqualTo(200));
        });
    }

    [Test]
    public async Task CountDuplicatesOnceWhenChunking()
    {
        var splits = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToArray();
        var handler = GatekeeperLike();

        var result = await CreateClient(handler).CancelSplitsAsync(splits.Concat(splits));

        Assert.Multiple(() =>
        {
            Assert.That(handler.CallCount, Is.EqualTo(1));
            Assert.That(result.Decisions, Has.Count.EqualTo(100));
        });
    }

    [Test]
    public async Task ClassifyOnlyTheFailedChunksSplitsFromItsStatus()
    {
        var splits = Enumerable.Range(0, 250).Select(_ => Guid.NewGuid()).ToArray();
        var secondChunk = splits.Skip(100).Take(100).ToHashSet();
        var handler = GatekeeperLike(chunk => secondChunk.Contains(chunk[0])
            ? HttpStatusCode.ServiceUnavailable
            : HttpStatusCode.OK);

        var result = await CreateClient(handler).CancelSplitsAsync(splits);

        Assert.Multiple(() =>
        {
            Assert.That(handler.CallCount, Is.EqualTo(3));
            Assert.That(result.Decisions, Has.Count.EqualTo(250));
            Assert.That(result.Confirmed.Count(), Is.EqualTo(150));
            Assert.That(result.Retry.Select(d => d.SplitCorrelationId), Is.EquivalentTo(secondChunk));
            Assert.That(result.Terminal, Is.Empty);
            Assert.That(result.HttpStatusCode, Is.EqualTo(503));
        });
    }

    [Test]
    public void RejectAnEmptyRequest()
    {
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, "{}");

        Assert.Multiple(() =>
        {
            Assert.That(async () => await CreateClient(handler).CancelSplitsAsync([]), Throws.ArgumentException);
            Assert.That(async () => await CreateClient(handler).CancelSplitsAsync(null!), Throws.ArgumentNullException);
            Assert.That(handler.CallCount, Is.Zero);
        });
    }
}
