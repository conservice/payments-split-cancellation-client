namespace Conservice.Payments.SplitCancellation.Client.Contracts;

/// <summary>
/// The RFC 7807 <c>type</c> values in the 409 answers of Gatekeeper's single-split route,
/// <c>POST tasks/{splitCorrelation}/cancel</c>.
/// </summary>
/// <remarks>
/// This client calls <c>tasks/cancel-batch</c>, not the single-split route, because the
/// single-split route answers the same 409 type for a task in execution and a task that has
/// completed, so a caller cannot tell "ask again later" from "stop asking". The constants are
/// here so a caller that still uses the single-split route reads the same values Gatekeeper sends.
/// </remarks>
public static class CancelConflictProblem
{
    /// <summary>The task is in execution or completed, so it cannot be cancelled.</summary>
    public const string NotCancellableTypeUri = "urn:conservice:gatekeeper:problem:task-not-cancellable";

    /// <summary>The task's state changed between Gatekeeper's read and update; the same request retried usually succeeds.</summary>
    public const string StateChangedTypeUri = "urn:conservice:gatekeeper:problem:cancel-state-changed";
}
