namespace Conservice.Payments.SplitCancellation.Client.Contracts;

/// <summary>
/// Gatekeeper's per-split answer from <c>POST tasks/cancel-batch</c>.
/// </summary>
/// <remarks>
/// Gatekeeper serializes this enum as a number, so a value Gatekeeper adds later still
/// deserializes here as an undefined number instead of failing. Classify it with
/// <see cref="Classification.CancelOutcomeClassifier"/>, which answers Retry for any value it
/// does not know. The numbers are part of the wire contract: never reorder or renumber them,
/// and append new values at the end.
/// </remarks>
public enum SplitCancellationOutcome
{
    /// <summary>The task was cancelled.</summary>
    Cancelled = 0,

    /// <summary>The task was already cancelled before this request.</summary>
    AlreadyCancelled = 1,

    /// <summary>
    /// No task existed for the split. Only Gatekeeper versions before 2.22.0 answer this; later
    /// versions answer <see cref="CancelRegistered"/>. Kept so the numbers stay stable.
    /// </summary>
    TaskNotFound = 2,

    /// <summary>The task is in execution, so it cannot be cancelled now.</summary>
    NotCancellable = 3,

    /// <summary>The task's state changed while Gatekeeper was cancelling it.</summary>
    RaceCondition = 4,

    /// <summary>Gatekeeper rejected the split correlation (for example <see cref="Guid.Empty"/>).</summary>
    ValidationFailed = 5,

    /// <summary>Gatekeeper could not cancel the task because of a dependency or persistence failure.</summary>
    Failed = 6,

    /// <summary>
    /// The task already ran to completion and can never become cancellable. Reconcile the split
    /// with its execution outcome instead of asking again.
    /// </summary>
    Completed = 7,

    /// <summary>
    /// No task existed yet, so Gatekeeper recorded the cancellation and will drop a task that
    /// arrives later for this split. A confirmed cancel.
    /// </summary>
    CancelRegistered = 8,
}
