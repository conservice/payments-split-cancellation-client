using System.Collections.Frozen;
using System.Net;
using Conservice.Payments.SplitCancellation.Client.Contracts;

namespace Conservice.Payments.SplitCancellation.Client.Classification;

/// <summary>
/// The one mapping from Gatekeeper's cancel answers to <see cref="CancelClassification"/>.
/// </summary>
public static class CancelOutcomeClassifier
{
    private static readonly FrozenDictionary<SplitCancellationOutcome, CancelClassification> Known =
        new Dictionary<SplitCancellationOutcome, CancelClassification>
        {
            [SplitCancellationOutcome.Cancelled] = CancelClassification.Confirmed,
            [SplitCancellationOutcome.AlreadyCancelled] = CancelClassification.Confirmed,
            [SplitCancellationOutcome.CancelRegistered] = CancelClassification.Confirmed,

            [SplitCancellationOutcome.Completed] = CancelClassification.Terminal,
            [SplitCancellationOutcome.ValidationFailed] = CancelClassification.Terminal,

            // In execution: the task can only move on to Completed, so asking again converges.
            [SplitCancellationOutcome.NotCancellable] = CancelClassification.Retry,
            [SplitCancellationOutcome.RaceCondition] = CancelClassification.Retry,
            [SplitCancellationOutcome.Failed] = CancelClassification.Retry,

            // A Gatekeeper before 2.22.0 kept no record of the cancel, so a task could still arrive.
            [SplitCancellationOutcome.TaskNotFound] = CancelClassification.Retry,
        }.ToFrozenDictionary();

    /// <summary>
    /// Every outcome this version of the client knows, with its classification.
    /// </summary>
    public static IReadOnlyDictionary<SplitCancellationOutcome, CancelClassification> KnownClassifications => Known;

    /// <summary>
    /// Classifies one split's outcome. An outcome this client does not know classifies as
    /// <see cref="CancelClassification.Retry"/>.
    /// </summary>
    public static CancelClassification Classify(SplitCancellationOutcome outcome) =>
        Known.TryGetValue(outcome, out var classification) ? classification : CancelClassification.Retry;

    /// <summary>
    /// Classifies an outcome only if this client knows it.
    /// </summary>
    /// <returns><c>true</c> when the outcome has an explicit classification.</returns>
    public static bool TryClassifyKnown(SplitCancellationOutcome outcome, out CancelClassification classification) =>
        Known.TryGetValue(outcome, out classification);

    /// <summary>
    /// Classifies a non-200 answer from <c>tasks/cancel-batch</c>, which applies to every split
    /// in the request. Every non-200 status classifies as <see cref="CancelClassification.Retry"/>:
    /// the answer says nothing about the splits' state, and the tasks behind it are most likely
    /// still cancellable.
    /// </summary>
    /// <remarks>
    /// This includes 400. Gatekeeper answers 400 only for an empty list or more than 100 distinct
    /// splits, and the client sends neither, so a 400 means a contract break or a proxy rejected
    /// the request, not that the cancel can never be confirmed. See
    /// <see cref="IsConfigurationOrContractFault"/> for the statuses that are logged as errors.
    /// </remarks>
    public static CancelClassification ClassifyBatchFailureStatus(HttpStatusCode statusCode) =>
        CancelClassification.Retry;

    /// <summary>
    /// Whether a non-200 answer points to a fault that asking again will not fix on its own:
    /// 400 (contract break or proxy), 401 or 403 (wrong audience or role), 404 (wrong base address).
    /// Splits still classify as Retry, but the client logs these at Error so they are noticed.
    /// </summary>
    public static bool IsConfigurationOrContractFault(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden
            or HttpStatusCode.NotFound;
}
