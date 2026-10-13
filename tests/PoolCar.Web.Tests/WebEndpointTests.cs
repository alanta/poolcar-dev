using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PoolCar.Web.Auth;
using PoolCar.Web.Clients;
using PoolCar.Web.Tests.Support;

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
        var redirect = new Uri(new Uri("https://localhost"), response.Headers.Location);
        var query = QueryHelpers.ParseQuery(redirect.Query);
        Assert.Equal("/", query["post_login_redirect_uri"]);
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
        var redirect = new Uri(new Uri("https://localhost"), response.Headers.Location);
        var query = QueryHelpers.ParseQuery(redirect.Query);
        Assert.Equal("/?param=value", query["post_login_redirect_uri"]);
    }

    [Fact]
    public async Task AnonymousRequest_WithAmpersandInQuery_KeepsReturnPathAsSingleParameter()
    {
        // Arrange
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Act
        using var response = await client.GetAsync("/?param=value&next=other", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var redirect = new Uri(new Uri("https://localhost"), response.Headers.Location);
        var query = QueryHelpers.ParseQuery(redirect.Query);
        Assert.Equal("/?param=value&next=other", query["post_login_redirect_uri"]);
        Assert.False(query.ContainsKey("next"));
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
    public async Task AuthenticatedRequest_ToRootPage_RendersLayoutForEmployee()
    {
        // Arrange
        var reservationsHandler = KnownEmployeeHandler();
        using var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<ReservationsApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => reservationsHandler);
            });
        });

        using var client = customFactory.CreateClient();
        client.DefaultRequestHeaders.Add(
            EasyAuthAuthenticationDefaults.HeaderName,
            TestPrincipals.Header(TestPrincipals.KnownId, "Bram Testbeek", PoolCarRoles.FleetOperator));

        // Act
        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Hoi Bram", html);
        Assert.DoesNotContain("Object-ID", html);
        Assert.Contains("<html lang=\"nl\">", html);
        Assert.Contains("name=\"request-verification-token\"", html);
        Assert.Contains("hx-boost=\"true\"", html);
        Assert.Contains("RequestVerificationToken", html);
        Assert.Contains("htmx", html);
        Assert.Contains("Thuislocatie niet ingesteld", html);
        Assert.Contains("Afmelden", html);
        Assert.Contains("Balie", html);

        var request = Assert.Single(reservationsHandler.Requests);
        Assert.Equal(TestPrincipals.KnownId.ToString(), request.Headers[ActingUserHandler.EmployeeHeader]);
        Assert.Equal("true", request.Headers[ActingUserHandler.OperatorHeader]);
    }

    [Theory]
    [InlineData(PoolCarRoles.FleetOperator, true)]
    [InlineData("Employee", false)]
    public async Task OperatorPolicy_RequiresFleetOperatorRole(string role, bool expected)
    {
        var authorization = _factory.Services.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(
            TestPrincipals.Create(TestPrincipals.KnownId, roles: role),
            PoolCarPolicies.Operator);

        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public async Task ReservationsClient_DoesNotRetryWrites()
    {
        var handler = new StubHttpHandler()
            .On(HttpMethod.Post, "/reservations/now", _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var customFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddHttpClient<ReservationsApiClient>()
                .ConfigurePrimaryHttpMessageHandler(() => handler)));
        using var scope = customFactory.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ReservationsApiClient>();

        var result = await client.BookNowAsync("HPN72V", TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(503, result.Problem.Status);
        Assert.Single(handler.Requests);
    }

    private static StubHttpHandler KnownEmployeeHandler() => new StubHttpHandler().OnJson(
        HttpMethod.Get,
        $"/employees/{TestPrincipals.KnownId}",
        $$"""{"id":"{{TestPrincipals.KnownId}}","name":"Bram Testbeek","homeOffice":"utrecht","kind":"Seeded"}""");

    [Theory]
    [InlineData("/wagenpark", "/cars?office=utrecht")]
    [InlineData("/wagenpark?vestiging=amsterdam", "/cars?office=amsterdam")]
    [InlineData("/wagenpark?vestiging=onbekend", "/cars?office=utrecht")]
    public async Task AuthenticatedRequest_ToWagenpark_RendersCarsOfSelectedOfficeGroupedByCategory(
        string path,
        string expectedCarsRequest)
    {
        // Arrange
        var fleetHandler = new TestFleetHandler();
        using var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<FleetApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => fleetHandler);
                services.AddHttpClient<ReservationsApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(KnownEmployeeHandler);
            });
        });

        using var client = customFactory.CreateClient();
        client.DefaultRequestHeaders.Add(
            EasyAuthAuthenticationDefaults.HeaderName,
            TestPrincipals.Header(TestPrincipals.KnownId));

        // Act
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedCarsRequest, fleetHandler.RequestedPaths);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("HPN-72-V", html);
        Assert.Contains("V-123-AB", html);
        Assert.Contains("Kia Niro", html);
        Assert.Contains("460 km", html);
        Assert.Contains("href=\"/wagenpark\"", html);
        Assert.DoesNotContain("Luxe", html);
        Assert.True(html.IndexOf(">Middel<", StringComparison.Ordinal) < html.IndexOf(">Kleine bestelbus<", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StaticAssets_ServePinnedHtmxAndInterFont()
    {
        using var client = _factory.CreateClient();

        using var tokensResponse = await client.GetAsync("/css/tokens.css", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, tokensResponse.StatusCode);
        var tokens = await tokensResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("--color-black: #1A1A1A;", tokens);
        Assert.Contains("--color-blue-700: #037F91;", tokens);
        Assert.Contains("--color-success: #219653;", tokens);

        using var siteStylesResponse = await client.GetAsync(
            "/css/site.css",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, siteStylesResponse.StatusCode);
        var siteStyles = await siteStylesResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("font-family: Inter", siteStyles);

        using var htmxResponse = await client.GetAsync(
            "/lib/htmx/htmx-2.0.11.min.js",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, htmxResponse.StatusCode);
        var htmx = await htmxResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.StartsWith("var htmx=", htmx);

        using var fontResponse = await client.GetAsync(
            "/fonts/InterVariable.woff2",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, fontResponse.StatusCode);
        Assert.True(fontResponse.Content.Headers.ContentLength > 0);
    }

    [Fact]
    public void RequestLocalization_UsesDutchCulture()
    {
        var options = _factory.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;

        Assert.Equal("nl-NL", options.DefaultRequestCulture.Culture.Name);
        Assert.Equal("nl-NL", options.SupportedCultures?.Single().Name);
        Assert.Equal("nl-NL", options.SupportedUICultures?.Single().Name);
    }

    private sealed class TestFleetHandler : HttpMessageHandler
    {
        public List<string> RequestedPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            lock (RequestedPaths)
            {
                RequestedPaths.Add(path);
            }

            var json = request.RequestUri.AbsolutePath switch
            {
                "/offices" => """
                    [{"id":"utrecht","name":"Utrecht","address":"Utrecht"},
                     {"id":"amsterdam","name":"Amsterdam","address":"Amsterdam"}]
                    """,
                "/categories" => """
                    [{"id":"kleine-bestelbus","name":"Kleine bestelbus","description":"Bus","sortOrder":4},
                     {"id":"middel","name":"Middel","description":"Gezinsauto","sortOrder":2},
                     {"id":"luxe","name":"Luxe","description":"Comfort","sortOrder":3}]
                    """,
                "/cars" => """
                    [{"plate":"V123AB","make":"Renault","model":"Kangoo","variant":"E-Tech","category":"kleine-bestelbus",
                      "office":"utrecht","colour":"Wit","seats":2,"rangeKm":285,"powerKw":90,
                      "firstRegistration":"2023-05-01","listPrice":null},
                     {"plate":"HPN72V","make":"Kia","model":"Niro","variant":"EV","category":"middel",
                      "office":"utrecht","colour":"Grijs","seats":5,"rangeKm":460,"powerKw":150,
                      "firstRegistration":"2024-03-01","listPrice":42950.00}]
                    """,
                _ => "[]"
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
