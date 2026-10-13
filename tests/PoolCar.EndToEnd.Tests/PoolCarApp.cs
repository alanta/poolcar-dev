using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace PoolCar.EndToEnd.Tests;

/// <summary>Starts the AppHost and a headless Chromium browser once for all end-to-end tests.</summary>
public sealed class PoolCarApp : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private DistributedApplication? _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    /// <summary>The address of the EasyAuth proxy, the signed-in entry point of the web app.</summary>
    public Uri ProxyUrl { get; private set; } = null!;

    public IBrowser Browser => _browser ?? throw new InvalidOperationException("The browser has not been started.");

    public async ValueTask InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(StartupTimeout);

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PoolCar_AppHost>(
            ["--Simulator:Enabled=false"],
            timeout.Token);
        UseRandomProxyPort(builder);
        UseFreshDatabase(builder);

        _app = await builder.BuildAsync(timeout.Token);
        await _app.StartAsync(timeout.Token);
        await WaitUntilHealthyAsync(_app, timeout.Token);
        ProxyUrl = _app.GetEndpoint(FindProxy(_app).Name, "http");

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    // The AppHost pins the proxy to port 5000 for local use; tests must not clash with a running instance.
    private static void UseRandomProxyPort(IDistributedApplicationTestingBuilder builder)
    {
        foreach (var endpoint in builder.Resources.OfType<IEasyAuthProxyResource>()
                     .SelectMany(proxy => proxy.Annotations.OfType<EndpointAnnotation>()))
        {
            endpoint.Port = null;
        }
    }

    // The AppHost keeps PostgreSQL data in a volume. Tests start from an empty database instead, so the seeded
    // reservations are relative to today and the developer's local data is left alone.
    private static void UseFreshDatabase(IDistributedApplicationTestingBuilder builder)
    {
        foreach (var container in builder.Resources.OfType<ContainerResource>())
        {
            foreach (var volume in container.Annotations.OfType<ContainerMountAnnotation>()
                         .Where(mount => mount.Type == ContainerMountType.Volume)
                         .ToList())
            {
                container.Annotations.Remove(volume);
            }
        }
    }

    private static async Task WaitUntilHealthyAsync(DistributedApplication app, CancellationToken cancellationToken)
    {
        var resources = app.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .Where(resource => resource is not ParameterResource)
            .Where(resource => !resource.Annotations.OfType<ExplicitStartupAnnotation>().Any());

        await Task.WhenAll(resources.Select(resource =>
            app.ResourceNotifications.WaitForResourceHealthyAsync(resource.Name, cancellationToken)));
    }

    private static IResource FindProxy(DistributedApplication app) =>
        app.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<IEasyAuthProxyResource>()
            .Single();
}

[CollectionDefinition(Name)]
public sealed class PoolCarAppCollection : ICollectionFixture<PoolCarApp>
{
    public const string Name = "PoolCar app";
}
