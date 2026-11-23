namespace Conservice.Payments.SplitCancellation.Client;

/// <summary>
/// Cancels payment splits at Gatekeeper through <c>POST tasks/cancel-batch</c> and classifies
/// every answer.
/// </summary>
/// <remarks>
/// The client does not throw for anything Gatekeeper or the network does: a failed request, a
/// timeout, a non-200 answer or an unreadable body becomes a classification for every split in
/// the request. It throws only for invalid arguments and when the caller's token is cancelled.
/// </remarks>
public interface IGatekeeperCancelClient
{
    /// <summary>
    /// Cancels the given splits. Duplicate correlations are sent once. Gatekeeper accepts at most
    /// 100 splits per call, so a larger request is sent as several calls of up to 100; each call's
    /// answer classifies only its own splits, and the decisions come back in request order.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="splitCorrelations"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="splitCorrelations"/> is empty.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<CancelSplitsResult> CancelSplitsAsync(
        IEnumerable<Guid> splitCorrelations,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels one split.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<SplitCancelDecision> CancelSplitAsync(
        Guid splitCorrelation,
        CancellationToken cancellationToken = default);
}
