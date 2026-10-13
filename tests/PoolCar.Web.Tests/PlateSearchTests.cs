using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PoolCar.Web.Auth;
using PoolCar.Web.Clients;
using PoolCar.Web.Tests.Support;
using Shouldly;

namespace PoolCar.Web.Tests;

public class PlateSearchTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string OfficesJson = """
        [{"id":"utrecht","name":"Utrecht","address":"Utrecht"},
         {"id":"amsterdam","name":"Amsterdam","address":"Amsterdam"}]
        """;

    private const string KiaJson = """
        {"plate":"HPN72V","make":"Kia","model":"Niro","variant":"EV","category":"middel","office":"utrecht",
         "colour":"Grijs","seats":5,"rangeKm":460,"powerKw":150,"firstRegistration":"2024-03-01","listPrice":null}
        """;

    private const string RenaultJson = """
        {"plate":"ZT472V","make":"Renault","model":"5","variant":"E-Tech","category":"klein","office":"amsterdam",
         "colour":"Geel","seats":4,"rangeKm":400,"powerKw":110,"firstRegistration":"2025-01-01","listPrice":null}
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Search_WithoutQuery_ShowsOnlyTheSearchField()
    {
        var (client, fleet) = CreateClient();

        var html = await client.GetStringAsync("/zoeken", Token);

        html.ShouldContain("<h1>Kenteken zoeken</h1>");
        html.ShouldContain("id=\"zoek-kenteken\" name=\"kenteken\"");
        html.ShouldNotContain("empty-state");
        fleet.Requests.ShouldNotContain(r => r.Uri.AbsolutePath == "/cars/search");
    }

    [Fact]
    public async Task Search_WithOneMatch_RedirectsToCarPage()
    {
        var (client, _) = CreateClient();

        using var response = await client.GetAsync("/zoeken?kenteken=HPN72V", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe("/auto/HPN72V");
    }

    [Fact]
    public async Task Search_WithSeveralMatches_ListsCarsLinkingToCarPage()
    {
        var (client, fleet) = CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/zoeken?kenteken=72V", Token));

        fleet.Requests.ShouldContain(r => r.PathAndQuery == "/cars/search?q=72V");
        html.ShouldContain("2 auto's gevonden voor '72V'");
        html.ShouldContain("<a class=\"card car-card\" href=\"/auto/HPN72V\">");
        html.ShouldContain("<span class=\"plate\">HPN-72-V</span>");
        html.ShouldContain("Kia Niro");
        html.ShouldContain("Vestiging Utrecht");
        html.ShouldContain("<a class=\"card car-card\" href=\"/auto/ZT472V\">");
        html.ShouldContain("Vestiging Amsterdam");
        html.ShouldContain("value=\"72V\"");
    }

    [Fact]
    public async Task Search_WithoutMatches_ShowsEmptyStateWithSearchFieldAndWagenparkLink()
    {
        var (client, _) = CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/zoeken?kenteken=QQ00QQ", Token));

        html.ShouldContain("Geen auto gevonden voor 'QQ00QQ'.");
        html.ShouldContain("id=\"zoek-kenteken\" name=\"kenteken\" type=\"search\" value=\"QQ00QQ\"");
        html.ShouldContain("href=\"/wagenpark\">Naar het Wagenpark</a>");
    }

    private (HttpClient Client, StubHttpHandler Fleet) CreateClient()
    {
        var fleet = new StubHttpHandler()
            .OnJson(HttpMethod.Get, "/offices", OfficesJson)
            .On(HttpMethod.Get, "/cars/search", request => StubHttpHandler.Json(request.Uri.Query switch
            {
                "?q=HPN72V" => $"[{KiaJson}]",
                "?q=72V" => $"[{KiaJson},{RenaultJson}]",
                _ => "[]",
            }));
        var reservations = new StubHttpHandler().OnJson(
            HttpMethod.Get,
            $"/employees/{TestPrincipals.KnownId}",
            $$"""{"id":"{{TestPrincipals.KnownId}}","name":"Bram Testbeek","homeOffice":"utrecht","kind":"Seeded"}""");
        var customFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddHttpClient<FleetApiClient>().ConfigurePrimaryHttpMessageHandler(() => fleet);
            services.AddHttpClient<ReservationsApiClient>().ConfigurePrimaryHttpMessageHandler(() => reservations);
        }));
        var client = customFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(
            EasyAuthAuthenticationDefaults.HeaderName,
            TestPrincipals.Header(TestPrincipals.KnownId, "Bram Testbeek"));
        return (client, fleet);
    }
}
