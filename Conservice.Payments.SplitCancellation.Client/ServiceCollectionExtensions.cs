using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Conservice.Payments.SplitCancellation.Client;

/// <summary>
/// Registers <see cref="IGatekeeperCancelClient"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IGatekeeperCancelClient"/> with <typeparamref name="TTokenProvider"/>
    /// as its <see cref="IGatekeeperAccessTokenProvider"/>, unless one is already registered.
    /// </summary>
    /// <returns>The client's <see cref="IHttpClientBuilder"/>, to add handlers such as resilience.</returns>
    public static IHttpClientBuilder AddGatekeeperCancelClient<TTokenProvider>(
        this IServiceCollection services,
        Action<GatekeeperCancelClientOptions> configure)
        where TTokenProvider : class, IGatekeeperAccessTokenProvider
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<IGatekeeperAccessTokenProvider, TTokenProvider>();
        return services.AddGatekeeperCancelClient(configure);
    }

    /// <summary>
    /// Registers <see cref="IGatekeeperCancelClient"/>. The caller registers an
    /// <see cref="IGatekeeperAccessTokenProvider"/> itself.
    /// </summary>
    /// <returns>The client's <see cref="IHttpClientBuilder"/>, to add handlers such as resilience.</returns>
    public static IHttpClientBuilder AddGatekeeperCancelClient(
        this IServiceCollection services,
        Action<GatekeeperCancelClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<GatekeeperCancelClientOptions>()
            .Configure(configure)
            .Validate(o => o.BaseAddress is { IsAbsoluteUri: true },
                "GatekeeperCancelClientOptions.BaseAddress must be an absolute URI.")
            .Validate(o => o.Timeout > TimeSpan.Zero || o.Timeout == Timeout.InfiniteTimeSpan,
                "GatekeeperCancelClientOptions.Timeout must be positive.")
            .ValidateOnStart();

        return services.AddHttpClient<IGatekeeperCancelClient, GatekeeperCancelClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<GatekeeperCancelClientOptions>>().Value;
            client.BaseAddress = WithTrailingSlash(options.BaseAddress!);
            client.Timeout = options.Timeout;
        });
    }

    internal static Uri WithTrailingSlash(Uri baseAddress) =>
        baseAddress.AbsoluteUri.EndsWith('/') ? baseAddress : new Uri(baseAddress.AbsoluteUri + "/");
}
