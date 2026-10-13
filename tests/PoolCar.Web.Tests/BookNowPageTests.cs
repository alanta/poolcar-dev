using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PoolCar.Web.Auth;
using PoolCar.Web.Availability;
using PoolCar.Web.Clients;
using PoolCar.Web.Tests.Support;
using Shouldly;

namespace PoolCar.Web.Tests;

public partial class BookNowPageTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    // Instants on Tuesday 13 October 2026: 11:00, 15:00 and 22:00 in Amsterdam
    private static readonly DateTimeOffset Morning = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Afternoon = new(2026, 10, 13, 13, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Evening = new(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);

    private const string OfficesJson = """
        [{"id":"utrecht","name":"Utrecht","address":"Utrecht"},{"id":"amsterdam","name":"Amsterdam","address":"Amsterdam"}]
        """;

    private const string NiroJson = """
        {"plate":"HPN72V","make":"Kia","model":"Niro","variant":"EV","category":"middel","office":"utrecht",
         "colour":"Grijs","seats":5,"rangeKm":460,"powerKw":150,"firstRegistration":"2024-03-01","listPrice":null}
        """;

    private readonly StubHttpHandler _fleet = FleetData.Fleet()
        .OnJson(HttpMethod.Get, "/offices", OfficesJson)
        .OnJson(HttpMethod.Get, "/cars/HPN72V", NiroJson);

    private readonly StubHttpHandler _reservations = new StubHttpHandler()
        .OnJson(
            HttpMethod.Get,
            $"/employees/{TestPrincipals.KnownId}",
            $$"""{"id":"{{TestPrincipals.KnownId}}","name":"Bram Testbeek","homeOffice":"utrecht","kind":"Seeded"}""")
        .OnJson(HttpMethod.Post, "/availability/now", """{"carIds":["CC22DD"]}""");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("2026-10-13T09:00:00Z", DayPart.Morning)]
    [InlineData("2026-10-13T13:00:00Z", DayPart.Afternoon)]
    [InlineData("2026-10-13T20:00:00Z", null)]
    [InlineData("2026-12-15T09:00:00Z", DayPart.Morning)]
    [InlineData("2026-12-15T13:00:00Z", DayPart.Afternoon)]
    [InlineData("2026-12-15T20:00:00Z", null)]
    public void CurrentDayPart_FollowsAmsterdamTime(string instant, DayPart? expected)
    {
        CurrentDayPart.At(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(expected);
    }

    [Fact]
    public async Task InTheMorning_ListsFreeCarsPerCategory_UntilNoon()
    {
        using var client = CreateClient(Morning);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/nu", Token));

        html.ShouldContain("<h2 class=\"book-now-until\">Vrij tot 12:00</h2>");
        html.ShouldContain("2 auto's nu vrij in Utrecht.");
        html.ShouldContain("Renault 5");
        html.ShouldContain("Kia Niro");
        html.ShouldNotContain("Peugeot e-208");
        html.IndexOf("id=\"nu-klein\"", StringComparison.Ordinal)
            .ShouldBeLessThan(html.IndexOf("id=\"nu-middel\"", StringComparison.Ordinal));
        html.ShouldContain("name=\"carId\" value=\"HPN72V\"");
        html.ShouldContain("Neem deze auto");
        html.ShouldNotContain("Nu kan je geen auto boeken.");
    }

    [Fact]
    public async Task InTheAfternoon_ShowsFreeUntilSix()
    {
        using var client = CreateClient(Afternoon);

        var html = await client.GetStringAsync("/nu", Token);

        html.ShouldContain("Vrij tot 18:00");
    }

    [Fact]
    public async Task OutsideHours_SuggestsReservingForTomorrowMorning()
    {
        using var client = CreateClient(Evening);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/nu?vestiging=amsterdam", Token));

        html.ShouldContain("Nu kan je geen auto boeken. Plan vooruit voor morgen.");
        html.ShouldContain("href=\"/reserveren?van=2026-10-14&dagdeel=ochtend&vestiging=amsterdam\"");
        html.ShouldNotContain("Neem deze auto");
        _reservations.Requests.ShouldNotContain(r => r.Uri.AbsolutePath == "/availability/now");
    }

    [Fact]
    public async Task OfficeFromQuery_IsUsed()
    {
        using var client = CreateClient(Morning);

        var html = await client.GetStringAsync("/nu?vestiging=amsterdam", Token);

        html.ShouldContain("<option value=\"amsterdam\" selected=\"selected\">Amsterdam</option>");
        _fleet.Requests.ShouldContain(r => r.PathAndQuery == "/cars?office=amsterdam");
    }

    [Fact]
    public async Task OfficeChange_WithHtmx_ReturnsOnlyTheList()
    {
        using var client = CreateClient(Morning);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/nu?vestiging=utrecht");
        request.Headers.Add("HX-Request", "true");

        using var response = await client.SendAsync(request, Token);
        var html = await response.Content.ReadAsStringAsync(Token);

        html.ShouldStartWith("<div id=\"nu-autos\"");
        html.ShouldNotContain("<html");
    }

    [Fact]
    public async Task TakingACar_RedirectsToMyReservationsWithToast()
    {
        _reservations.OnJson(HttpMethod.Post, "/reservations/now", BookedJson);
        _reservations.OnJson(HttpMethod.Get, "/reservations/mine", "[]");
        using var client = CreateClient(Morning);

        using var response = await PostAsync(client, "HPN72V", htmx: true);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.GetValues("HX-Redirect").ShouldHaveSingleItem().ShouldBe("/mijn-reserveringen");
        _reservations.Requests.ShouldContain(r => r.Uri.AbsolutePath == "/reservations/now" && r.Body!.Contains("\"HPN72V\""));
        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/mijn-reserveringen", Token));
        page.ShouldContain("Goede reis! De Kia Niro HPN-72-V staat voor je klaar.");
    }

    [Fact]
    public async Task TakingACar_WithoutJavaScript_Redirects()
    {
        _reservations.OnJson(HttpMethod.Post, "/reservations/now", BookedJson);
        using var client = CreateClient(Morning);

        using var response = await PostAsync(client, "HPN72V", htmx: false);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe("/mijn-reserveringen");
    }

    [Fact]
    public async Task Conflict_ShowsMessageAndRefreshedList()
    {
        _reservations.On(
            HttpMethod.Post,
            "/reservations/now",
            _ => StubHttpHandler.Problem(HttpStatusCode.Conflict, "conflict", "Deze auto is net door iemand anders genomen."));
        using var client = CreateClient(Morning);

        using var response = await PostAsync(client, "HPN72V", htmx: true);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        html.ShouldStartWith("<div id=\"nu-autos\"");
        html.ShouldContain("Deze auto is net door iemand anders genomen.");
        html.ShouldContain("Vrij tot 12:00");
        html.ShouldContain("Renault 5");
        _reservations.Requests.ShouldContain(r => r.Uri.AbsolutePath == "/availability/now");
    }

    [Fact]
    public async Task OutsideHoursAtTheApi_ShowsItsDetail()
    {
        _reservations.On(
            HttpMethod.Post,
            "/reservations/now",
            _ => StubHttpHandler.Problem(HttpStatusCode.UnprocessableEntity, "invalid", "Nu kan je geen auto boeken. Plan vooruit."));
        using var client = CreateClient(Morning);

        using var response = await PostAsync(client, "HPN72V", htmx: false);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));

        html.ShouldContain("<p class=\"form-error\" role=\"alert\">Nu kan je geen auto boeken. Plan vooruit.</p>");
        html.ShouldContain("<h1>Nu een auto</h1>");
    }

    private const string BookedJson = """
        {"id":"11111111-1111-1111-1111-111111111111","employeeId":"f0671165-c417-421a-9eac-649e8534de6e",
         "carId":"HPN72V","from":"2026-10-13","to":"2026-10-13","dayPart":"Morning","source":"User",
         "createdAt":"2026-10-13T09:00:00+00:00","cancelledAt":null,"notPickedUp":false,"overdue":false,
         "periods":[{"id":"22222222-2222-2222-2222-222222222222","date":"2026-10-13",
           "start":"2026-10-13T09:00:00+00:00","end":"2026-10-13T10:00:00+00:00","status":"PickedUp",
           "pickedUpAt":"2026-10-13T09:00:00+00:00","returnedAt":null,"notPickedUp":false,"overdue":false}]}
        """;

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string carId, bool htmx)
    {
        var page = await client.GetStringAsync("/thuislocatie", Token);
        var token = TokenPattern().Match(page).Groups[1].Value;
        var request = new HttpRequestMessage(HttpMethod.Post, "/nu")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["vestiging"] = "utrecht",
                ["carId"] = carId,
            }),
        };
        if (htmx)
        {
            request.Headers.Add("HX-Request", "true");
        }

        return await client.SendAsync(request, Token);
    }

    private HttpClient CreateClient(DateTimeOffset now)
    {
        var customFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(now));
            services.AddHttpClient<FleetApiClient>().ConfigurePrimaryHttpMessageHandler(() => _fleet);
            services.AddHttpClient<ReservationsApiClient>().ConfigurePrimaryHttpMessageHandler(() => _reservations);
        }));
        var client = customFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(
            EasyAuthAuthenticationDefaults.HeaderName,
            TestPrincipals.Header(TestPrincipals.KnownId, "Bram Testbeek"));
        return client;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}
