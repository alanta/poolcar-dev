using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PoolCar.Web.Auth;
using PoolCar.Web.Clients;
using PoolCar.Web.Tests.Support;
using Shouldly;

namespace PoolCar.Web.Tests;

public class CarPageTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string OfficesJson = """[{"id":"utrecht","name":"Utrecht","address":"Utrecht"}]""";

    private const string CarJson = """
        {"plate":"HPN72V","make":"Kia","model":"Niro","variant":"EV","category":"middel","office":"utrecht",
         "colour":"Grijs","seats":5,"rangeKm":460,"powerKw":150,"firstRegistration":"2024-03-01","listPrice":null}
        """;

    private const string MyReservationsJson = """
        [{"id":"11111111-1111-1111-1111-111111111111","employeeId":"f0671165-c417-421a-9eac-649e8534de6e",
          "carId":"HPN72V","from":"2026-10-13","to":"2026-10-13","dayPart":"Afternoon","source":"User",
          "createdAt":"2026-10-12T08:00:00+00:00","cancelledAt":null,"notPickedUp":false,"overdue":false,"periods":[]}]
        """;

    private const string ScheduleJson = """
        [{"reservationId":"11111111-1111-1111-1111-111111111111","periodId":"a1111111-1111-1111-1111-111111111111",
          "carId":"HPN72V","date":"2026-10-13","dayPart":"Afternoon","start":"2026-10-13T10:00:00+00:00",
          "end":"2026-10-13T16:00:00+00:00","status":"Scheduled","source":"User","employeeFirstName":"Bram",
          "notPickedUp":false,"overdue":false,"atRisk":false},
         {"reservationId":"22222222-2222-2222-2222-222222222222","periodId":"a2222222-2222-2222-2222-222222222222",
          "carId":"HPN72V","date":"2026-10-14","dayPart":"FullDay","start":"2026-10-14T06:00:00+00:00",
          "end":"2026-10-14T16:00:00+00:00","status":"Scheduled","source":"User","employeeFirstName":"Anna",
          "notPickedUp":false,"overdue":false,"atRisk":false}]
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CarPage_ShowsCarDetailsAndReserveLink()
    {
        var (client, _) = CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/auto/HPN72V", Token));

        html.ShouldContain("<h1>Kia Niro</h1>");
        html.ShouldContain("HPN-72-V");
        html.ShouldContain("<span>Middel</span>");
        html.ShouldContain("class=\"category-icon\"");
        html.ShouldContain("<dt>Vestiging</dt><dd>Utrecht</dd>");
        html.ShouldContain("<dt>Kleur</dt><dd>Grijs</dd>");
        html.ShouldContain("<dt>Bereik</dt><dd>460 km</dd>");
        html.ShouldContain("<dt>Zitplaatsen</dt><dd>5</dd>");
        html.ShouldContain("<dt>Vermogen</dt><dd>150 kW</dd>");
        html.ShouldContain("href=\"/reserveren?auto=HPN72V&vestiging=utrecht\">Reserveer deze auto</a>");
    }

    [Fact]
    public async Task CarPage_ShowsThisWeeksOccupancyWithTextLabelsAndLegend()
    {
        var (client, reservations) = CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/auto/HPN72V", Token));

        var schedule = reservations.Requests.Single(r => r.Uri.AbsolutePath == "/schedule");
        schedule.Body.ShouldNotBeNull();
        schedule.Body.ShouldContain("\"from\":\"2026-10-12\"");
        html.ShouldContain("ma 12 – zo 18 okt");
        html.ShouldContain("<li class=\"week-day is-today\" aria-current=\"date\">");
        html.ShouldContain("""<span class="week-slot slot-mine">""");
        html.ShouldContain("Middag: Jouw reservering");
        html.ShouldContain("Ochtend: Gereserveerd");
        html.ShouldContain("Middag: Gereserveerd");
        html.ShouldContain("Ochtend: Vrij");
        html.ShouldContain("aria-label=\"Legenda\"");
    }

    [Fact]
    public async Task CarPage_ListsUpcomingReservations()
    {
        var (client, _) = CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/auto/HPN72V", Token));

        html.ShouldContain("<td>Di 13 okt</td>");
        html.ShouldContain("<td>Middag, 12:00–18:00</td>");
        html.ShouldContain("<span class=\"badge badge-mine\">Jij</span>");
        html.ShouldContain("<td>Wo 14 okt</td>");
        html.ShouldContain("<td>Hele dag, 08:00–18:00</td>");
        html.ShouldContain("Anna");
        html.ShouldContain(">Gereserveerd</span>");
    }

    [Fact]
    public async Task CarPage_WithoutReservations_SuggestsReserving()
    {
        var (client, _) = CreateClient(scheduleJson: "[]");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/auto/HPN72V", Token));

        html.ShouldContain("Deze auto is de komende weken nog niet gereserveerd.");
    }

    [Fact]
    public async Task UnknownCar_ShowsNotFoundWithSearchForm()
    {
        var (client, _) = CreateClient();

        using var response = await client.GetAsync("/auto/XX99XX", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
        html.ShouldContain("Geen auto met kenteken XX99XX gevonden.");
        html.ShouldContain("<form method=\"get\" action=\"/zoeken\" class=\"search-form\" role=\"search\">");
        html.ShouldContain("id=\"zoek-kenteken\" name=\"kenteken\" type=\"search\" value=\"XX99XX\"");
    }

    private (HttpClient Client, StubHttpHandler Reservations) CreateClient(string scheduleJson = ScheduleJson)
    {
        var fleet = new StubHttpHandler()
            .OnJson(HttpMethod.Get, "/offices", OfficesJson)
            .OnJson(HttpMethod.Get, "/categories", FleetData.CategoriesJson)
            .OnJson(HttpMethod.Get, "/cars/HPN72V", CarJson);
        var reservations = new StubHttpHandler()
            .OnJson(
                HttpMethod.Get,
                $"/employees/{TestPrincipals.KnownId}",
                $$"""{"id":"{{TestPrincipals.KnownId}}","name":"Bram Testbeek","homeOffice":"utrecht","kind":"Seeded"}""")
            .OnJson(HttpMethod.Get, "/reservations/mine", MyReservationsJson)
            .OnJson(HttpMethod.Post, "/schedule", scheduleJson);
        var customFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero)));
            services.AddHttpClient<FleetApiClient>().ConfigurePrimaryHttpMessageHandler(() => fleet);
            services.AddHttpClient<ReservationsApiClient>().ConfigurePrimaryHttpMessageHandler(() => reservations);
        }));
        var client = customFactory.CreateClient();
        client.DefaultRequestHeaders.Add(
            EasyAuthAuthenticationDefaults.HeaderName,
            TestPrincipals.Header(TestPrincipals.KnownId, "Bram Testbeek"));
        return (client, reservations);
    }
}
