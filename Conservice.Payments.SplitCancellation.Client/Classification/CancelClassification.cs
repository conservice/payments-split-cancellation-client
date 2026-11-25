namespace Conservice.Payments.SplitCancellation.Client.Classification;

/// <summary>
/// What a Gatekeeper cancel answer means for the payment.
/// </summary>
/// <remarks>
/// Only <see cref="Confirmed"/> allows a caller to fail or redirect the payment. <see cref="Retry"/>
/// is the default value, so an unset or unrecognised classification never lets a payment be failed.
/// </remarks>
public enum CancelClassification
{
    /// <summary>
    /// Not resolved yet. Do not act on the payment; ask Gatekeeper again later. Also the answer
    /// for anything this client does not recognise.
    /// </summary>
    Retry = 0,

    /// <summary>
    /// The split will not execute. The caller may fail or redirect the payment.
    /// </summary>
    Confirmed = 1,

    /// <summary>
    /// Asking again will never confirm the cancel. Stop asking, do not fail the payment, reconcile
    /// the split with its execution outcome and alert.
    /// </summary>
    Terminal = 2,
}
