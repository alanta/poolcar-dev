using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PoolCar.Web.Auth;
using PoolCar.Web.Clients;
using PoolCar.Web.Tests.Support;
using Shouldly;

namespace PoolCar.Web.Tests;

public partial class ReservePageTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string OfficesJson = """
        [{"id":"utrecht","name":"Utrecht","address":"Utrecht"},
         {"id":"amsterdam","name":"Amsterdam","address":"Amsterdam"}]
        """;

    private const string NiroJson = """
        {"plate":"HPN72V","make":"Kia","model":"Niro","variant":"EV","category":"middel","office":"utrecht",
         "colour":"Grijs","seats":5,"rangeKm":460,"powerKw":150,"firstRegistration":"2024-03-01","listPrice":null}
        """;

    private const string RenaultJson = """
        {"plate":"AA11BB","make":"Renault","model":"5","variant":"E-Tech","category":"klein","office":"utrecht",
         "colour":"Geel","seats":4,"rangeKm":400,"powerKw":110,"firstRegistration":"2025-01-01","listPrice":null}
        """;

    private const string ReservationId = "11111111-1111-1111-1111-111111111111";

    // 11:00 in Amsterdam on Tuesday 13 October 2026
    private static readonly DateTimeOffset Morning = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly StubHttpHandler _fleet = FleetData.Fleet()
        .OnJson(HttpMethod.Get, "/offices", OfficesJson)
        .OnJson(HttpMethod.Get, "/cars/HPN72V", NiroJson)
        .OnJson(HttpMethod.Get, "/cars/AA11BB", RenaultJson);

    private readonly StubHttpHandler _reservations = new StubHttpHandler()
        .OnJson(
            HttpMethod.Get,
            $"/employees/{TestPrincipals.KnownId}",
            $$"""{"id":"{{TestPrincipals.KnownId}}","name":"Bram Testbeek","homeOffice":"utrecht","kind":"Seeded"}""")
        .OnJson(HttpMethod.Post, "/availability/busy", """{"carIds":[]}""")
        .OnJson(HttpMethod.Post, "/schedule", "[]");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(9, "2026-10-13", "ochtend")]
    [InlineData(11, "2026-10-13", "middag")]
    [InlineData(16, "2026-10-14", "ochtend")]
    public async Task Defaults_AreHomeOfficeAndToday_OrTomorrowAfterSix(int utcHour, string expectedDate, string expectedDayPart)
    {
        using var client = CreateClient(new DateTimeOffset(2026, 10, 13, utcHour, 0, 0, TimeSpan.Zero));

        var html = await client.GetStringAsync("/reserveren", Token);

        html.ShouldContain($"id=\"van\" name=\"van\" type=\"date\" value=\"{expectedDate}\"");
        html.ShouldContain($"id=\"tot\" name=\"tot\" type=\"date\" value=\"{expectedDate}\"");
        html.ShouldContain($"value=\"{expectedDayPart}\" checked=\"checked\"");
        html.ShouldContain("<option value=\"utrecht\" selected=\"selected\">Utrecht</option>");
    }

    [Fact]
    public async Task Summary_DescribesTheChoice()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync("/reserveren?van=2026-10-13&tot=2026-10-14&dagdeel=ochtend", Token);

        html.ShouldContain("<p class=\"reserve-summary\">Di 13 en wo 14 oktober, ochtend, Utrecht</p>");
    }

    [Theory]
    [InlineData("2026-10-14", "2026-10-13", "De einddatum ligt voor de begindatum. Kies een einddatum vanaf wo 14 okt.")]
    [InlineData("2026-10-13", "2026-10-27", "Je kan maximaal 14 dagen achter elkaar reserveren. Kies een kortere periode.")]
    [InlineData("2026-10-12", "2026-10-12", "Je kan niet in het verleden reserveren. Kies vandaag of later.")]
    [InlineData("13-10-2026", "2026-10-13", "Kies een geldige datum.")]
    public async Task InvalidDates_ShowMessage_WithoutAskingAvailability(string from, string to, string message)
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync($"/reserveren?van={from}&tot={to}&dagdeel=ochtend", Token);

        html.ShouldContain($"<p class=\"form-error\" role=\"alert\">{message}</p>");
        html.ShouldNotContain("Kies een categorie");
        _reservations.Requests.ShouldNotContain(r => r.Uri.AbsolutePath == "/availability/busy");
    }

    [Fact]
    public async Task FourteenDays_AreAllowed()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync("/reserveren?van=2026-10-13&tot=2026-10-26&dagdeel=ochtend", Token);

        html.ShouldNotContain("form-error");
        html.ShouldContain("Kies een categorie");
        var busy = _reservations.Requests.Where(r => r.Uri.AbsolutePath == "/availability/busy").ShouldHaveSingleItem();
        busy.Body!.ShouldContain("\"from\":\"2026-10-13\",\"to\":\"2026-10-26\",\"dayPart\":\"Morning\"");
    }

    [Fact]
    public async Task Categories_ShowAvailableCountsAndExamples_AndZeroState()
    {
        Busy("CC22DD");
        using var client = CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/reserveren?van=2026-10-14&dagdeel=ochtend", Token));

        var klein = TileHtml(html, "Klein");
        klein.ShouldContain("bijv. Renault 5");
        klein.ShouldNotContain("Peugeot");
        klein.ShouldContain("1 van 2 vrij");
        klein.ShouldContain("href=\"/reserveren?vestiging=utrecht&van=2026-10-14&tot=2026-10-14&dagdeel=ochtend&categorie=klein\"");

        var middel = TileHtml(html, "Middel");
        middel.ShouldContain("bijv. Kia Niro");
        middel.ShouldContain("1 van 1 vrij");

        var luxe = TileHtml(html, "Luxe");
        luxe.ShouldContain("class=\"tile is-disabled\"");
        luxe.ShouldContain("Geen auto vrij. Probeer een andere vestiging of dag.");
        luxe.ShouldContain("<a href=\"/reserveren?vestiging=amsterdam&van=2026-10-14&tot=2026-10-14&dagdeel=ochtend&categorie=luxe\">Kijk in Amsterdam</a>");
    }

    [Fact]
    public async Task Cars_AreSortedByRange_WithChooseLinks()
    {
        using var client = CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/reserveren?van=2026-10-14&dagdeel=ochtend&categorie=klein", Token));

        html.IndexOf("AA-11-BB", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("CC-22-DD", StringComparison.Ordinal));
        html.ShouldContain("Geel · 400 km bereik · 4 zitplaatsen");
        html.ShouldContain("href=\"/reserveren?vestiging=utrecht&van=2026-10-14&tot=2026-10-14&dagdeel=ochtend&categorie=klein&auto=AA11BB\"");
        html.ShouldNotContain("Bevestig je reservering");
    }

    [Fact]
    public async Task QueryString_RoundTrips_ThroughFormAndLinks()
    {
        using var client = CreateClient();
        const string query = "vestiging=utrecht&van=2026-10-14&tot=2026-10-15&dagdeel=middag&categorie=klein&auto=AA11BB";

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/reserveren?{query}", Token));

        html.ShouldContain("id=\"van\" name=\"van\" type=\"date\" value=\"2026-10-14\"");
        html.ShouldContain("id=\"tot\" name=\"tot\" type=\"date\" value=\"2026-10-15\"");
        html.ShouldContain("value=\"middag\" checked=\"checked\"");
        html.ShouldContain("<input type=\"hidden\" name=\"categorie\" value=\"klein\" form=\"reserve-form\" />");
        html.ShouldContain("<input type=\"hidden\" name=\"auto\" value=\"AA11BB\" form=\"reserve-form\" />");
        html.ShouldContain($"href=\"/reserveren?vestiging=utrecht&van=2026-10-14&tot=2026-10-15&dagdeel=middag&categorie=klein&auto=CC22DD\"");
        html.ShouldContain("Wo 14 en do 15 oktober, middag, Utrecht");
        html.ShouldContain("Bevestig je reservering");
        html.ShouldContain("Middag, 12:00–18:00, elke dag");
        html.ShouldContain("<input type=\"hidden\" name=\"auto\" value=\"AA11BB\" />");
    }

    [Fact]
    public async Task HtmxRequest_GetsOnlyTheSteps()
    {
        using var client = CreateClient();
        client.DefaultRequestHeaders.Add("HX-Request", "true");

        var html = await client.GetStringAsync("/reserveren?van=2026-10-14&dagdeel=ochtend&categorie=klein", Token);

        html.ShouldNotContain("<html");
        html.ShouldNotContain("id=\"reserve-form\"");
        html.ShouldContain("Kies een auto");
        html.ShouldContain("autofocus=\"autofocus\"");
    }

    [Fact]
    public async Task MoreThanSixCars_ShowsSix_AndShowAllLink()
    {
        var cars = Enumerable.Range(1, 8).Select(i => $$"""
            {"plate":"KL{{i:00}}AB","make":"Renault","model":"5","variant":"","category":"klein","office":"utrecht",
             "colour":"Geel","seats":4,"rangeKm":{{300 + i}},"powerKw":110,"firstRegistration":"2025-01-01","listPrice":null}
            """);
        _fleet.OnJson(HttpMethod.Get, "/cars", $"[{string.Join(',', cars)}]");
        using var client = CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/reserveren?van=2026-10-14&dagdeel=ochtend&categorie=klein", Token));
        var all = WebUtility.HtmlDecode(await client.GetStringAsync("/reserveren?van=2026-10-14&dagdeel=ochtend&categorie=klein&alle=true", Token));

        html.ShouldContain("KL-08-AB");
        html.ShouldNotContain("KL-02-AB");
        html.ShouldContain(">Toon alle 8 auto's</a>");
        all.ShouldContain("KL-01-AB");
        all.ShouldNotContain("Toon alle");
    }

    [Fact]
    public async Task Reserving_ShowsConfirmationAndToast()
    {
        _reservations.On(HttpMethod.Post, "/reservations", _ => StubHttpHandler.Json(ReservationJson("AA11BB"), HttpStatusCode.Created));
        using var client = CreateClient();
        await AddTokenAsync(client);
        client.DefaultRequestHeaders.Add("HX-Request", "true");

        using var response = await PostReserveAsync(client, "klein", "AA11BB");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
        html.ShouldContain("Gereserveerd!");
        html.ShouldContain("Haal de sleutel op bij de balie in Utrecht, vanaf 08:00 op wo 14 oktober.");
        html.ShouldContain("href=\"/mijn-reserveringen\"");
        html.ShouldContain("hx-swap-oob=\"beforeend:#toasts\"");
        html.ShouldContain("Renault 5 AA-11-BB is gereserveerd.");
        var post = _reservations.Requests.Where(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath == "/reservations").ShouldHaveSingleItem();
        post.Body.ShouldBe("""{"carId":"AA11BB","from":"2026-10-14","to":"2026-10-14","dayPart":"Morning","source":null}""");
    }

    [Fact]
    public async Task Reserving_WithoutJavaScript_RedirectsToConfirmation()
    {
        _reservations
            .On(HttpMethod.Post, "/reservations", _ => StubHttpHandler.Json(ReservationJson("AA11BB"), HttpStatusCode.Created))
            .OnJson(HttpMethod.Get, $"/reservations/{ReservationId}", ReservationJson("AA11BB"));
        using var client = CreateClient();
        await AddTokenAsync(client);

        using var response = await PostReserveAsync(client, "klein", "AA11BB");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe($"/reserveren?gereserveerd={ReservationId}");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync(response.Headers.Location.OriginalString, Token));
        html.ShouldContain("Gereserveerd!");
        html.ShouldContain("Haal de sleutel op bij de balie in Utrecht, vanaf 08:00 op wo 14 oktober.");
        html.ShouldContain("Renault 5 AA-11-BB is gereserveerd.");
    }

    [Fact]
    public async Task Conflict_WhilePosting_ShowsWarning_AndReloadsRemainingCars()
    {
        var posted = false;
        _reservations
            .On(HttpMethod.Post, "/availability/busy", _ => StubHttpHandler.Json(posted ? """{"carIds":["AA11BB"]}""" : """{"carIds":[]}"""))
            .On(HttpMethod.Post, "/reservations", _ =>
            {
                posted = true;
                return Conflict409();
            });
        using var client = CreateClient();
        await AddTokenAsync(client);
        client.DefaultRequestHeaders.Add("HX-Request", "true");

        using var response = await PostReserveAsync(client, "klein", "AA11BB");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
        html.ShouldContain("class=\"section card reserve-conflict\"");
        html.ShouldContain("Deze auto is net door iemand anders gereserveerd voor wo 14 oktober.");
        html.ShouldNotContain("Bevestig je reservering");
        html.ShouldContain("Kies een auto");
        html.ShouldNotContain("AA-11-BB");
        html.ShouldContain("CC-22-DD");
        html.ShouldContain("href=\"/reserveren?vestiging=utrecht&van=2026-10-14&tot=2026-10-14&dagdeel=ochtend&categorie=klein\"");
    }

    [Fact]
    public async Task Conflict_WhilePostingPreselectedCar_LinksBackToCategories()
    {
        _reservations.On(HttpMethod.Post, "/reservations", _ => Conflict409());
        using var client = CreateClient();
        await AddTokenAsync(client);
        client.DefaultRequestHeaders.Add("HX-Request", "true");

        using var response = await PostReserveAsync(client, null, "HPN72V");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
        html.ShouldContain("Deze auto is net door iemand anders gereserveerd voor wo 14 oktober.");
        html.ShouldContain("<a class=\"button button-dark\" href=\"/reserveren?vestiging=utrecht&van=2026-10-14&tot=2026-10-14&dagdeel=ochtend\">Kies een andere auto</a>");
        html.ShouldNotContain("Kies een categorie");
        html.ShouldNotContain("Reserveer <span");
    }

    [Fact]
    public async Task PreselectedCar_SkipsCategoryAndCarSteps()
    {
        using var client = CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/reserveren?auto=HPN72V&vestiging=amsterdam", Token));

        html.ShouldNotContain("Kies een categorie");
        html.ShouldNotContain("Kies een auto</h2>");
        html.ShouldContain("Bevestig je reservering");
        html.ShouldContain("Kia Niro <span class=\"plate\">HPN-72-V</span>");
        html.ShouldContain("<span class=\"reserve-fixed\">Utrecht</span>");
        html.ShouldContain("<input type=\"hidden\" name=\"auto\" value=\"HPN72V\" form=\"reserve-form\" />");
        html.ShouldNotContain("name=\"categorie\"");
        html.ShouldContain("href=\"/reserveren?vestiging=utrecht&van=2026-10-13&tot=2026-10-13&dagdeel=ochtend\">Kies een andere auto</a>");
    }

    [Fact]
    public async Task PreselectedCar_ThatIsTaken_ShowsConflictBeforePosting()
    {
        Busy("HPN72V");
        _reservations.OnJson(HttpMethod.Post, "/schedule", ScheduleJson("2026-10-15", "Morning"));
        using var client = CreateClient();

        using var response = await client.GetAsync(
            "/reserveren?auto=HPN72V&vestiging=utrecht&van=2026-10-14&tot=2026-10-16&dagdeel=hele-dag",
            Token);

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
        html.ShouldContain("class=\"section card reserve-conflict\"");
        html.ShouldContain("Deze auto is al gereserveerd voor do 15 oktober.");
        html.ShouldContain("href=\"/reserveren?vestiging=utrecht&van=2026-10-14&tot=2026-10-16&dagdeel=hele-dag\">Kies een andere auto</a>");
        html.ShouldNotContain("Bevestig je reservering");
    }

    [Fact]
    public async Task PostingPreselectedCar_ThatIsTaken_DoesNotPost()
    {
        Busy("HPN72V");
        using var client = CreateClient();
        await AddTokenAsync(client);
        client.DefaultRequestHeaders.Add("HX-Request", "true");

        using var response = await PostReserveAsync(client, null, "HPN72V");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain("Deze auto is niet vrij in deze periode.");
        _reservations.Requests.ShouldNotContain(r => r.Uri.AbsolutePath == "/reservations");
    }

    [Fact]
    public async Task Invalid_FromApi_ShowsDutchDetail()
    {
        _reservations.On(HttpMethod.Post, "/reservations", _ =>
            StubHttpHandler.Problem(HttpStatusCode.UnprocessableEntity, "invalid", "Kies een dagdeel."));
        using var client = CreateClient();
        await AddTokenAsync(client);
        client.DefaultRequestHeaders.Add("HX-Request", "true");

        using var response = await PostReserveAsync(client, "klein", "AA11BB");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain("<p class=\"form-error\" role=\"alert\">Kies een dagdeel.</p>");
    }

    [Fact]
    public async Task UnknownPlate_ShowsMessage_AndCategories()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync("/reserveren?auto=XX99XX", Token);

        html.ShouldContain("Geen auto met kenteken XX-99-XX gevonden. Kies hieronder een auto.");
        html.ShouldContain("Kies een categorie");
    }

    private void Busy(params string[] plates) =>
        _reservations.OnJson(HttpMethod.Post, "/availability/busy", $$"""{"carIds":[{{string.Join(',', plates.Select(p => $"\"{p}\""))}}]}""");

    private static HttpResponseMessage Conflict409() =>
        StubHttpHandler.Problem(HttpStatusCode.Conflict, "conflict", "De auto is al gereserveerd.", ",\"conflictingDates\":[\"2026-10-14\"]");

    private static string ReservationJson(string plate) => $$"""
        {"id":"{{ReservationId}}","employeeId":"{{TestPrincipals.KnownId}}","carId":"{{plate}}",
         "from":"2026-10-14","to":"2026-10-14","dayPart":"Morning","source":"User",
         "createdAt":"2026-10-13T09:00:00+00:00","cancelledAt":null,"notPickedUp":false,"overdue":false,"periods":[]}
        """;

    private static string ScheduleJson(string date, string dayPart) => $$"""
        [{"reservationId":"{{ReservationId}}","periodId":"22222222-2222-2222-2222-222222222222","carId":"HPN72V",
          "date":"{{date}}","dayPart":"{{dayPart}}","start":"{{date}}T06:00:00+00:00","end":"{{date}}T10:00:00+00:00",
          "status":"Scheduled","source":"User","employeeFirstName":"Fleur","notPickedUp":false,"overdue":false,"atRisk":false}]
        """;

    private static string TileHtml(string html, string name)
    {
        var title = html.IndexOf($"<span class=\"tile-title\">{name}</span>", StringComparison.Ordinal);
        var start = html.LastIndexOf("<li>", title, StringComparison.Ordinal);
        var end = html.IndexOf("</li>", title, StringComparison.Ordinal);
        return html[start..end];
    }

    private static Task<HttpResponseMessage> PostReserveAsync(HttpClient client, string? category, string plate)
    {
        var form = new Dictionary<string, string>
        {
            ["vestiging"] = "utrecht",
            ["van"] = "2026-10-14",
            ["tot"] = "2026-10-14",
            ["dagdeel"] = "ochtend",
            ["auto"] = plate,
        };
        if (category is not null)
        {
            form["categorie"] = category;
        }

        return client.PostAsync("/reserveren?handler=Reserve", new FormUrlEncodedContent(form), Token);
    }

    private static async Task AddTokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/reserveren?van=2026-10-30", Token);
        client.DefaultRequestHeaders.Add("RequestVerificationToken", TokenPattern().Match(html).Groups[1].Value);
    }

    private HttpClient CreateClient(DateTimeOffset? now = null)
    {
        var customFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(now ?? Morning));
            services.AddHttpClient<FleetApiClient>().ConfigurePrimaryHttpMessageHandler(() => _fleet);
            services.AddHttpClient<ReservationsApiClient>().ConfigurePrimaryHttpMessageHandler(() => _reservations);
        }));
        var client = customFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(
            EasyAuthAuthenticationDefaults.HeaderName,
            TestPrincipals.Header(TestPrincipals.KnownId, "Bram Testbeek"));
        return client;
    }

    [GeneratedRegex("name=\"request-verification-token\" content=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}
