using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Conservice.Payments.SplitCancellation.Client.Tests;

[TestFixture]
public class ServiceCollectionExtensionsShould
{
    private sealed class FixedTokenProvider : IGatekeeperAccessTokenProvider
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult<string?>("token");
    }

    [Test]
    public async Task RegisterAClientThatCallsTheConfiguredAddress()
    {
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, "{\"results\":[]}");
        var services = new ServiceCollection().AddLogging();
        services
            .AddGatekeeperCancelClient<FixedTokenProvider>(o => o.BaseAddress = new Uri("https://gatekeeper.test/api/v1"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IGatekeeperCancelClient>().CancelSplitAsync(Guid.NewGuid());

        Assert.That(handler.LastRequest!.RequestUri, Is.EqualTo(new Uri("https://gatekeeper.test/api/v1/tasks/cancel-batch")));
    }

    [Test]
    public void KeepATokenProviderTheServiceAlreadyRegistered()
    {
        var existing = StubTokenProvider.Returning("mine");
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IGatekeeperAccessTokenProvider>(existing);

        services.AddGatekeeperCancelClient<FixedTokenProvider>(o => o.BaseAddress = new Uri("https://gatekeeper.test/"));

        using var provider = services.BuildServiceProvider();
        Assert.That(provider.GetRequiredService<IGatekeeperAccessTokenProvider>(), Is.SameAs(existing));
    }

    [Test]
    public void RejectAMissingBaseAddress()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddGatekeeperCancelClient<FixedTokenProvider>(_ => { });

        using var provider = services.BuildServiceProvider();

        Assert.That(() => provider.GetRequiredService<IGatekeeperCancelClient>(), Throws.InstanceOf<OptionsValidationException>());
    }

    [Test]
    public void RejectANonPositiveTimeout()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddGatekeeperCancelClient<FixedTokenProvider>(o =>
        {
            o.BaseAddress = new Uri("https://gatekeeper.test/");
            o.Timeout = TimeSpan.Zero;
        });

        using var provider = services.BuildServiceProvider();

        Assert.That(() => provider.GetRequiredService<IGatekeeperCancelClient>(), Throws.InstanceOf<OptionsValidationException>());
    }

    [TestCase("https://gatekeeper.test/api/v1", "https://gatekeeper.test/api/v1/")]
    [TestCase("https://gatekeeper.test/api/v1/", "https://gatekeeper.test/api/v1/")]
    [TestCase("https://gatekeeper.test", "https://gatekeeper.test/")]
    public void AddATrailingSlashToTheBaseAddress(string configured, string expected)
    {
        Assert.That(ServiceCollectionExtensions.WithTrailingSlash(new Uri(configured)), Is.EqualTo(new Uri(expected)));
    }
}
