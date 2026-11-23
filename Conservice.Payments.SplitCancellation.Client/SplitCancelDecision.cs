using Conservice.Payments.SplitCancellation.Client.Classification;
using Conservice.Payments.SplitCancellation.Client.Contracts;

namespace Conservice.Payments.SplitCancellation.Client;

/// <summary>
/// The classified answer for one split.
/// </summary>
/// <param name="SplitCorrelationId">The split correlation the decision is for.</param>
/// <param name="Classification">What the answer means for the payment.</param>
/// <param name="Outcome">
/// Gatekeeper's outcome for the split, or <c>null</c> when Gatekeeper gave none: the request
/// failed, Gatekeeper answered non-200, or the 200 answer had no result for this split.
/// </param>
/// <param name="Message">Gatekeeper's message, or a description of why there is no outcome.</param>
public sealed record SplitCancelDecision(
    Guid SplitCorrelationId,
    CancelClassification Classification,
    SplitCancellationOutcome? Outcome,
    string Message)
{
    /// <summary>The split will not execute; the payment may be failed or redirected.</summary>
    public bool IsConfirmed => Classification == CancelClassification.Confirmed;

    /// <summary>Asking again will never confirm; reconcile with the execution outcome.</summary>
    public bool IsTerminal => Classification == CancelClassification.Terminal;

    /// <summary>Not resolved yet; do not act on the payment and ask again later.</summary>
    public bool IsRetry => Classification == CancelClassification.Retry;
}

/// <summary>
/// The classified answers for every split in one cancel request, in request order.
/// </summary>
/// <param name="Decisions">One decision per distinct split correlation requested.</param>
/// <param name="HttpStatusCode">Gatekeeper's HTTP status, or <c>null</c> when no answer arrived.</param>
public sealed record CancelSplitsResult(
    IReadOnlyList<SplitCancelDecision> Decisions,
    int? HttpStatusCode)
{
    /// <summary>The splits whose cancel is confirmed.</summary>
    public IEnumerable<SplitCancelDecision> Confirmed => Decisions.Where(d => d.IsConfirmed);

    /// <summary>The splits that will never be confirmed.</summary>
    public IEnumerable<SplitCancelDecision> Terminal => Decisions.Where(d => d.IsTerminal);

    /// <summary>The splits to ask about again later.</summary>
    public IEnumerable<SplitCancelDecision> Retry => Decisions.Where(d => d.IsRetry);

    /// <summary><c>true</c> when every requested split is confirmed.</summary>
    public bool AllConfirmed => Decisions.All(d => d.IsConfirmed);

    internal static CancelSplitsResult ForAll(
        IReadOnlyList<Guid> splitCorrelations,
        CancelClassification classification,
        int? httpStatusCode,
        string message) =>
        new(
            splitCorrelations.Select(id => new SplitCancelDecision(id, classification, null, message)).ToArray(),
            httpStatusCode);
}
