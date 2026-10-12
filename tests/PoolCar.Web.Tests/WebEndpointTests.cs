using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PoolCar.Web.Auth;
using PoolCar.Web.Clients;

namespace PoolCar.Web.Tests;

public class WebEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WebEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AnonymousRequest_ToRootPage_RedirectsToEasyAuthLogin()
    {
        // Arrange
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Act
        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal("/.auth/login/aad?post_login_redirect_uri=/", response.Headers.Location.ToString());
    }

    [Fact]
    public async Task AnonymousRequest_ToPageWithQuery_RedirectsWithQuery()
    {
        // Arrange
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Act
        using var response = await client.GetAsync("/?param=value", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal("/.auth/login/aad?post_login_redirect_uri=/?param=value", response.Headers.Location.ToString());
    }

    [Fact]
    public async Task AnonymousRequest_ToHealth_ReturnsOk()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousRequest_ToAlive_ReturnsOk()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/alive", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedRequest_ToRootPage_RendersUserAndApiHealthOk()
    {
        // Arrange
        using var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<FleetClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new TestHealthHandler());
                services.AddHttpClient<ReservationsClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new TestHealthHandler());
            });
        });

        using var client = customFactory.CreateClient();
        var header = EasyAuthPrincipalParser.CreateHeader(
            name: "Jan de Boer",
            objectId: "3fa85f64-5717-4562-b3fc-2c963f66afa6",
            roles: ["FleetOperator"]);

        client.DefaultRequestHeaders.Add(EasyAuthAuthenticationDefaults.HeaderName, header);

        // Act
        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Hallo Jan de Boer", html);
        Assert.Contains("3fa85f64-5717-4562-b3fc-2c963f66afa6", html);
        Assert.Contains("FleetOperator", html);
        Assert.Contains("Fleet: ok, Reservations: ok", html);
    }

    private sealed class TestHealthHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Healthy")
            });
        }
    }
}
