using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PoolCar.Web.Auth;
using PoolCar.Web.Clients;
using PoolCar.Web.Tests.Support;
using Shouldly;

namespace PoolCar.Web.Tests;

public class HomePageTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string OfficesJson = """[{"id":"utrecht","name":"Utrecht","address":"Utrecht"}]""";

    private const string CarJson = """
        {"plate":"HPN72V","make":"Kia","model":"Niro","variant":"EV","category":"middel","office":"utrecht",
         "colour":"Grijs","seats":5,"rangeKm":460,"powerKw":150,"firstRegistration":"2024-03-01","listPrice":null}
        """;

    private const string ReservationsJson = """
        [{"id":"11111111-1111-1111-1111-111111111111","employeeId":"f0671165-c417-421a-9eac-649e8534de6e",
          "carId":"HPN72V","from":"2026-10-14","to":"2026-10-14","dayPart":"Afternoon","source":"User",
          "createdAt":"2026-10-12T08:00:00+00:00","cancelledAt":null,"notPickedUp":false,"overdue":false,
          "periods":[{"id":"22222222-2222-2222-2222-222222222222","date":"2026-10-14",
            "start":"2026-10-14T11:00:00+00:00","end":"2026-10-14T16:00:00+00:00","status":"Scheduled",
            "pickedUpAt":null,"returnedAt":null,"notPickedUp":false,"overdue":false}]}]
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Home_ShowsGreetingAndEmployeeTasks_WithoutBalie()
    {
        using var client = CreateClient("[]");

        var html = await client.GetStringAsync("/", Token);

        html.ShouldContain("Hoi Bram");
        html.ShouldContain("<span class=\"tile-title\">Auto reserveren</span>");
        html.ShouldContain("<span class=\"tile-title\">Nu een auto</span>");
        html.ShouldContain("<span class=\"tile-title\">Mijn reserveringen</span>");
        html.ShouldContain("<span class=\"tile-title\">Kenteken zoeken</span>");
        html.ShouldContain("<span class=\"tile-title\">Planning</span>");
        html.ShouldContain("<span class=\"tile-title\">Wagenpark</span>");
        html.ShouldNotContain("<span class=\"tile-title\">Balie</span>");
    }

    [Fact]
    public async Task Home_ShowsBalieTileForOperators()
    {
        using var client = CreateClient("[]", PoolCarRoles.FleetOperator);

        var html = await client.GetStringAsync("/", Token);

        html.ShouldContain("<span class=\"tile-title\">Balie</span>");
    }

    [Fact]
    public async Task Home_WithoutReservation_SuggestsReserving()
    {
        using var client = CreateClient("[]");

        var html = await client.GetStringAsync("/", Token);

        html.ShouldContain("Je hebt geen reservering gepland.");
        html.ShouldContain("<a class=\"button\" href=\"/reserveren\">Auto reserveren</a>");
    }

    [Fact]
    public async Task Home_ShowsNextReservation()
    {
        using var client = CreateClient(ReservationsJson);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/", Token));

        html.ShouldContain("Je volgende reservering");
        html.ShouldContain("Kia Niro <span class=\"plate\">HPN-72-V</span>");
        html.ShouldContain("Woensdag 14 oktober 2026 · Middag, 12:00–18:00");
        html.ShouldContain("Vestiging Utrecht");
        html.ShouldNotContain("Je hebt geen reservering gepland.");
    }

    private HttpClient CreateClient(string reservationsJson, params string[] roles)
    {
        var fleet = new StubHttpHandler()
            .OnJson(HttpMethod.Get, "/offices", OfficesJson)
            .OnJson(HttpMethod.Get, "/cars/HPN72V", CarJson);
        var reservations = new StubHttpHandler()
            .OnJson(
                HttpMethod.Get,
                $"/employees/{TestPrincipals.KnownId}",
                $$"""{"id":"{{TestPrincipals.KnownId}}","name":"Bram Testbeek","homeOffice":"utrecht","kind":"Seeded"}""")
            .OnJson(HttpMethod.Get, "/reservations/mine", reservationsJson);
        var customFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero)));
            services.AddHttpClient<FleetApiClient>().ConfigurePrimaryHttpMessageHandler(() => fleet);
            services.AddHttpClient<ReservationsApiClient>().ConfigurePrimaryHttpMessageHandler(() => reservations);
        }));
        var client = customFactory.CreateClient();
        client.DefaultRequestHeaders.Add(
            EasyAuthAuthenticationDefaults.HeaderName,
            TestPrincipals.Header(TestPrincipals.KnownId, "Bram Testbeek", roles));
        return client;
    }
}
