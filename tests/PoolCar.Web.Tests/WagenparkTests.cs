using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PoolCar.Web.Auth;
using PoolCar.Web.Clients;
using PoolCar.Web.Pages;
using PoolCar.Web.Tests.Support;
using Shouldly;

namespace PoolCar.Web.Tests;

public class WagenparkTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string OfficesJson = """
        [{"id":"utrecht","name":"Utrecht","address":"Utrecht"},
         {"id":"amsterdam","name":"Amsterdam","address":"Amsterdam"}]
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void CategoryGroup_SummarisesCountAndAverageRange()
    {
        var category = new FleetCategory("middel", "Middel", "Gezinsauto", 2);
        var group = new CategoryGroup(category, [Car("A", 460), Car("B", 501)]);

        group.Summary.ShouldBe("Middel, 2 auto's, gemiddeld 481 km bereik");
    }

    [Fact]
    public void CategoryGroup_UsesSingularForOneCar()
    {
        var group = new CategoryGroup(new FleetCategory("luxe", "Luxe", "Comfort", 3), [Car("A", 600)]);

        group.Summary.ShouldBe("Luxe, 1 auto, gemiddeld 600 km bereik");
    }

    [Fact]
    public void CategoryGroup_Create_KeepsCategoryOrderAndSkipsEmptyCategories()
    {
        FleetCategory[] categories =
        [
            new("middel", "Middel", "", 2),
            new("klein", "Klein", "", 1),
            new("luxe", "Luxe", "", 3),
        ];

        var groups = CategoryGroup.Create(categories, [Car("A", 400, "middel"), Car("B", 300, "klein")]);

        groups.Select(g => g.Category.Id).ShouldBe(["klein", "middel"]);
    }

    [Fact]
    public async Task Wagenpark_DefaultsToHomeOffice_AndShowsCarCardsPerCategory()
    {
        var (client, fleet) = CreateClient("amsterdam");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/wagenpark", Token));

        fleet.Requests.ShouldContain(r => r.PathAndQuery == "/cars?office=amsterdam");
        html.ShouldContain("Klein, 2 auto's, gemiddeld 375 km bereik");
        html.ShouldContain("Middel, 1 auto, gemiddeld 460 km bereik");
        html.ShouldContain("<a class=\"card car-card\" href=\"/auto/HPN72V\">");
        html.ShouldContain("<span class=\"plate\">HPN-72-V</span>");
        html.ShouldContain("Grijs · 460 km bereik · 5 zitplaatsen");
        html.IndexOf("Klein, 2", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("Middel, 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Wagenpark_FiltersByOfficeAndCategory()
    {
        var (client, fleet) = CreateClient("utrecht");

        var html = await client.GetStringAsync("/wagenpark?vestiging=amsterdam&categorie=middel", Token);

        fleet.Requests.ShouldContain(r => r.PathAndQuery == "/cars?office=amsterdam&category=middel");
        html.ShouldContain("<option value=\"amsterdam\" selected=\"selected\">Amsterdam</option>");
        html.ShouldContain("<input type=\"radio\" name=\"categorie\" value=\"middel\" checked=\"checked\" />");
    }

    [Fact]
    public async Task Wagenpark_IgnoresUnknownCategory()
    {
        var (client, fleet) = CreateClient("utrecht");

        using var response = await client.GetAsync("/wagenpark?categorie=onbekend", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        fleet.Requests.ShouldContain(r => r.PathAndQuery == "/cars?office=utrecht");
    }

    [Fact]
    public async Task Wagenpark_HtmxFilterRequest_ReturnsOnlyTheCars()
    {
        var (client, _) = CreateClient("utrecht");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/wagenpark?vestiging=utrecht&categorie=");
        request.Headers.Add("HX-Request", "true");

        using var response = await client.SendAsync(request, Token);
        var html = await response.Content.ReadAsStringAsync(Token);

        html.ShouldContain("car-card");
        html.ShouldNotContain("<html");
        html.ShouldNotContain("<form");
    }

    [Fact]
    public async Task Wagenpark_WithoutCarsInCategory_SuggestsAnotherFilter()
    {
        var (client, _) = CreateClient("utrecht", carsJson: "[]");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/wagenpark?categorie=luxe", Token));

        html.ShouldContain("Er staan geen auto's in de categorie Luxe in Utrecht.");
        html.ShouldContain("Kies een andere vestiging of categorie.");
    }

    private static FleetCar Car(string plate, int rangeKm, string category = "middel") =>
        new(plate, "Kia", "Niro", "EV", category, "utrecht", "Grijs", 5, rangeKm, 150, new DateOnly(2024, 1, 1), null);

    private (HttpClient Client, StubHttpHandler Fleet) CreateClient(string homeOffice, string carsJson = FleetData.UtrechtCarsJson)
    {
        var fleet = new StubHttpHandler()
            .OnJson(HttpMethod.Get, "/offices", OfficesJson)
            .OnJson(HttpMethod.Get, "/categories", FleetData.CategoriesJson)
            .OnJson(HttpMethod.Get, "/cars", carsJson);
        var reservations = new StubHttpHandler().OnJson(
            HttpMethod.Get,
            $"/employees/{TestPrincipals.KnownId}",
            $$"""{"id":"{{TestPrincipals.KnownId}}","name":"Bram Testbeek","homeOffice":"{{homeOffice}}","kind":"Seeded"}""");
        var customFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddHttpClient<FleetApiClient>().ConfigurePrimaryHttpMessageHandler(() => fleet);
            services.AddHttpClient<ReservationsApiClient>().ConfigurePrimaryHttpMessageHandler(() => reservations);
        }));
        var client = customFactory.CreateClient();
        client.DefaultRequestHeaders.Add(
            EasyAuthAuthenticationDefaults.HeaderName,
            TestPrincipals.Header(TestPrincipals.KnownId, "Bram Testbeek"));
        return (client, fleet);
    }
}
