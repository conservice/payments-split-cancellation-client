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
    /// Cancels the given splits. Duplicate correlations are sent once.
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
