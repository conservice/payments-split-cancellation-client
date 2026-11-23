namespace Conservice.Payments.SplitCancellation.Client;

/// <summary>
/// Supplies the bearer token for calls to Gatekeeper. Each service implements it over its own
/// authentication (for example <c>Conservice.Platform.Authentication</c>'s <c>IAuthManager</c>),
/// so this package does not pin an authentication package version.
/// </summary>
public interface IGatekeeperAccessTokenProvider
{
    /// <summary>
    /// Returns an access token, or <c>null</c> when none is available. A <c>null</c> token or an
    /// exception classifies every split in the request as Retry.
    /// </summary>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken);
}
