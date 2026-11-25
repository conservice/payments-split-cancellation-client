namespace Conservice.Payments.SplitCancellation.Client;

/// <summary>
/// Settings for <see cref="IGatekeeperCancelClient"/>.
/// </summary>
public sealed class GatekeeperCancelClientOptions
{
    /// <summary>
    /// Gatekeeper's API root, the address that <c>tasks/cancel-batch</c> is appended to, for
    /// example <c>https://gatekeeper.example/api/v1/</c>. Required; a missing trailing slash is added.
    /// </summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// How long one cancel request may take. A request that times out classifies as Retry.
    /// Defaults to 30 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
