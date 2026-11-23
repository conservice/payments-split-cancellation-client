namespace Conservice.Payments.SplitCancellation.Client.Contracts;

/// <summary>
/// Body of Gatekeeper's <c>POST tasks/cancel-batch</c>.
/// </summary>
/// <param name="SplitCorrelations">The split correlations to cancel. Gatekeeper requires at least one.</param>
public sealed record CancelBatchRequest(IReadOnlyList<Guid> SplitCorrelations);

/// <summary>
/// Gatekeeper's 200 answer to <c>POST tasks/cancel-batch</c>: one result per split.
/// </summary>
/// <param name="Results">The per-split results.</param>
public sealed record CancelBatchResponse(IReadOnlyList<SplitCancellationResult>? Results);

/// <summary>
/// Gatekeeper's answer for one split in a batch cancel.
/// </summary>
/// <param name="SplitCorrelationId">The split correlation the result is for.</param>
/// <param name="Outcome">What Gatekeeper did with the split's task.</param>
/// <param name="Message">Gatekeeper's human-readable description of the outcome.</param>
public sealed record SplitCancellationResult(
    Guid SplitCorrelationId,
    SplitCancellationOutcome Outcome,
    string? Message);
