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
    /// in the request. 400 means Gatekeeper rejected the request itself, so sending it again
    /// cannot succeed; anything else (5xx, auth, gateway, unknown) may succeed later.
    /// </summary>
    public static CancelClassification ClassifyBatchFailureStatus(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.BadRequest ? CancelClassification.Terminal : CancelClassification.Retry;
}
