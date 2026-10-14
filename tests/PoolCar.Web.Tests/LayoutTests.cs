using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PoolCar.Web.Auth;
using PoolCar.Web.Clients;
using PoolCar.Web.Tests.Support;
using Polly.CircuitBreaker;
using Shouldly;

namespace PoolCar.Web.Tests;

public partial class LayoutTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string OfficesJson = """
        [{"id":"utrecht","name":"Utrecht","address":"Utrecht"},
         {"id":"amsterdam","name":"Amsterdam","address":"Amsterdam"}]
        """;

    private readonly StubHttpHandler _fleet = FleetData.Fleet().OnJson(HttpMethod.Get, "/offices", OfficesJson);

    private readonly StubHttpHandler _reservations = new StubHttpHandler()
        .OnJson(
            HttpMethod.Get,
            $"/employees/{TestPrincipals.KnownId}",
            $$"""{"id":"{{TestPrincipals.KnownId}}","name":"Bram Testbeek","homeOffice":"utrecht","kind":"Seeded"}""")
        .OnJson(HttpMethod.Get, "/reservations/mine", "[]")
        .On(HttpMethod.Put, $"/employees/{TestPrincipals.KnownId}", _ => StubHttpHandler.Json(
            $$"""{"id":"{{TestPrincipals.KnownId}}","name":"Bram Testbeek","homeOffice":"amsterdam","kind":"Seeded"}"""));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Navigation_MarksCurrentPage_AndHidesBalieForEmployees()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync("/wagenpark", Token);

        html.ShouldContain("<a href=\"/reserveren\">Reserveren</a>");
        html.ShouldContain("<a href=\"/nu\">Nu een auto</a>");
        html.ShouldContain("<a href=\"/mijn-reserveringen\">Mijn reserveringen</a>");
        html.ShouldContain("<a href=\"/planning\">Planning</a>");
        html.ShouldContain("<a href=\"/wagenpark\" aria-current=\"page\">Wagenpark</a>");
        html.ShouldNotContain("/balie");
        html.ShouldContain("action=\"/zoeken\"");
        html.ShouldContain("name=\"kenteken\"");
    }

    [Fact]
    public async Task Navigation_ShowsBalieForOperators()
    {
        using var client = CreateClient(PoolCarRoles.FleetOperator);

        var html = await client.GetStringAsync("/wagenpark", Token);

        html.ShouldContain("<a href=\"/balie\">Balie</a>");
    }

    [Fact]
    public async Task UnknownPage_ShowsDutchNotFoundPage()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/bestaat-niet", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var html = await response.Content.ReadAsStringAsync(Token);
        html.ShouldContain("Pagina niet gevonden");
        html.ShouldContain("Bram Testbeek");
    }

    [Fact]
    public async Task ForbiddenPage_SaysNoAccess()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync("/fout/403", Token);

        html.ShouldContain("Geen toegang");
    }

    [Fact]
    public async Task UnhandledException_ShowsErrorPageWithTraceId()
    {
        var failingFleet = new StubHttpHandler();
        using var production = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<FleetApiClient>().ConfigurePrimaryHttpMessageHandler(() => failingFleet);
                services.AddHttpClient<ReservationsApiClient>().ConfigurePrimaryHttpMessageHandler(() => _reservations);
            });
        });
        using var client = production.CreateClient();
        client.DefaultRequestHeaders.Add(EasyAuthAuthenticationDefaults.HeaderName, TestPrincipals.Header(TestPrincipals.KnownId));

        using var response = await client.GetAsync("/wagenpark", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var html = await response.Content.ReadAsStringAsync(Token);
        html.ShouldContain("Er ging iets mis");
        html.ShouldContain("class=\"trace-id\"");
    }

    [Fact]
    public async Task ErrorPage_RendersWithoutCallingTheApis()
    {
        var reservationCalls = 0;
        var failingReservations = new StubHttpHandler().On(HttpMethod.Get, $"/employees/{TestPrincipals.KnownId}", _ =>
        {
            Interlocked.Increment(ref reservationCalls);
            throw new BrokenCircuitException();
        });
        using var production = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<FleetApiClient>().ConfigurePrimaryHttpMessageHandler(() => _fleet);
                services.AddHttpClient<ReservationsApiClient>().ConfigurePrimaryHttpMessageHandler(() => failingReservations);
            });
        });
        using var client = production.CreateClient();
        client.DefaultRequestHeaders.Add(
            EasyAuthAuthenticationDefaults.HeaderName,
            TestPrincipals.Header(TestPrincipals.KnownId, "Bram Testbeek"));

        using var response = await client.GetAsync("/wagenpark", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var html = await response.Content.ReadAsStringAsync(Token);
        html.ShouldContain("Tijdelijk niet beschikbaar");
        html.ShouldContain("Bram Testbeek");
        html.ShouldNotContain("Thuislocatie:");
        reservationCalls.ShouldBe(1);
        _fleet.Requests.ShouldNotContain(r => r.Uri.AbsolutePath == "/offices");
    }

    [Fact]
    public async Task ChangingHomeOffice_SavesOffice_AndReturnsToPage()
    {
        using var client = CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await client.PostAsync("/thuislocatie", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["returnUrl"] = "/wagenpark",
            ["office"] = "amsterdam",
        }), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe("/wagenpark");
        var put = _reservations.Requests.Where(r => r.Method == HttpMethod.Put).ShouldHaveSingleItem();
        put.Body.ShouldBe("""{"name":"Bram Testbeek","homeOffice":"amsterdam"}""");
    }

    [Fact]
    public async Task ChangingHomeOffice_ToUnknownOffice_ShowsMessage()
    {
        using var client = CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await client.PostAsync("/thuislocatie", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["office"] = "rotterdam",
        }), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain("Kies een vestiging uit de lijst.");
        _reservations.Requests.ShouldNotContain(r => r.Method == HttpMethod.Put);
    }

    private HttpClient CreateClient(params string[] roles)
    {
        var customFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddHttpClient<FleetApiClient>().ConfigurePrimaryHttpMessageHandler(() => _fleet);
            services.AddHttpClient<ReservationsApiClient>().ConfigurePrimaryHttpMessageHandler(() => _reservations);
        }));
        var client = customFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(
            EasyAuthAuthenticationDefaults.HeaderName,
            TestPrincipals.Header(TestPrincipals.KnownId, "Bram Testbeek", roles));
        return client;
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/thuislocatie", Token);
        return TokenPattern().Match(html).Groups[1].Value;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}
