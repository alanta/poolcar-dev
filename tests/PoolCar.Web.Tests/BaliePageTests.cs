using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PoolCar.Web.Auth;
using PoolCar.Web.Clients;
using PoolCar.Web.Formatting;
using PoolCar.Web.Tests.Support;
using Shouldly;

namespace PoolCar.Web.Tests;

public partial class BaliePageTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string OfficesJson = """
        [{"id":"utrecht","name":"Utrecht","address":"Utrecht"},
         {"id":"amsterdam","name":"Amsterdam","address":"Amsterdam"}]
        """;

    // Wednesday 14 October, 11:00 in Amsterdam; periods use the real instants of their day parts
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid FleurPeriod = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid DaanPeriod = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid LottePeriod = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003");
    private static readonly Guid SannePeriod = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000004");
    private static readonly Guid JorisPeriod = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000005");
    private static readonly Guid IlsePeriod = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000006");
    private static readonly Guid RiskPeriod = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000007");

    private static readonly string ScheduleJson = $"""
        [{Entry(FleurPeriod, "AA11BB", "2026-10-14", "Morning", "Scheduled", "Fleur")},
         {Entry(DaanPeriod, "CC22DD", "2026-10-14", "Afternoon", "Scheduled", "Daan")},
         {Entry(LottePeriod, "HPN72V", "2026-10-14", "Morning", "Scheduled", "Lotte", notPickedUp: true)},
         {Entry(SannePeriod, "HPN72V", "2026-10-14", "FullDay", "PickedUp", "Sanne")},
         {Entry(JorisPeriod, "AA11BB", "2026-10-14", "Morning", "PickedUp", "Joris")},
         {Entry(Guid.NewGuid(), "CC22DD", "2026-10-14", "Morning", "Returned", "Mila")},
         {Entry(IlsePeriod, "CC22DD", "2026-10-13", "Afternoon", "PickedUp", "Ilse", overdue: true)},
         {Entry(RiskPeriod, "CC22DD", "2026-10-15", "Morning", "Scheduled", "Noor", atRisk: true)}]
        """;

    private readonly StubHttpHandler _fleet = FleetData.Fleet().OnJson(HttpMethod.Get, "/offices", OfficesJson);

    private readonly StubHttpHandler _reservations = new StubHttpHandler()
        .OnJson(
            HttpMethod.Get,
            $"/employees/{TestPrincipals.KnownId}",
            $$"""{"id":"{{TestPrincipals.KnownId}}","name":"Bram Testbeek","homeOffice":"utrecht","kind":"Seeded"}""")
        .OnJson(HttpMethod.Post, "/schedule", ScheduleJson)
        .OnJson(HttpMethod.Post, $"/periods/{FleurPeriod}/pickup", Reservation("AA11BB"))
        .OnJson(HttpMethod.Post, $"/periods/{IlsePeriod}/return", Reservation("CC22DD"));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("ophalen", "Fleur", "Lotte", "Daan")]
    [InlineData("onderweg", "Sanne")]
    [InlineData("inleveren", "Joris")]
    [InlineData("te-laat", "Ilse")]
    public async Task Board_AssignsPeriodsToSections(string section, params string[] names)
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync("/balie", Token);

        var items = ItemNames().Matches(Section(html, section)).Select(m => m.Groups[1].Value);
        items.ShouldBe(names);
    }

    [Fact]
    public async Task Board_CountsCarsOutReturnedAndOverdue()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync("/balie", Token);

        html.ShouldContain("<strong>2 uit, 1 terug, 1 te laat</strong>");
        html.ShouldContain($"bijgewerkt {AmsterdamTime.Time(Now)}");
        html.ShouldContain("hx-trigger=\"every 30s\"");
    }

    [Fact]
    public async Task PickUps_OnlyRunningPeriodsCanBeHandedOut()
    {
        using var client = CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/balie", Token));

        var fleur = Item(html, FleurPeriod);
        fleur.ShouldContain("Ochtend 08:00–12:00");
        fleur.ShouldContain("Renault 5");
        fleur.ShouldContain("AA-11-BB");
        fleur.ShouldContain("action=\"/balie?handler=PickUp\"");
        fleur.ShouldContain("hx-post=\"/balie?handler=PickUp\"");
        fleur.ShouldContain("name=\"__RequestVerificationToken\"");
        Item(html, DaanPeriod).ShouldContain("Uitgeven vanaf 12:00");
        Item(html, DaanPeriod).ShouldNotContain("<form");
        var lotte = Item(html, LottePeriod);
        lotte.ShouldContain("is-muted");
        lotte.ShouldContain("Niet opgehaald");
        lotte.ShouldNotContain("<form");
    }

    [Fact]
    public async Task ReturnActions_AreOfferedForDueAndOverdueCars()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync("/balie", Token);

        Item(html, JorisPeriod).ShouldContain("handler=Return");
        Item(html, IlsePeriod).ShouldContain("handler=Return");
        Item(html, SannePeriod).ShouldNotContain("<form");
        Item(html, SannePeriod).ShouldContain("Terug om 18:00");
    }

    [Fact]
    public async Task Overdue_ShowsDateAndBookingsAtRisk()
    {
        using var client = CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/balie", Token));

        var ilse = Item(html, IlsePeriod);
        ilse.ShouldContain("di 13 okt · Middag 12:00–18:00");
        ilse.ShouldContain("Boekingen in gevaar");
        ilse.ShouldContain("Noor · do 15 okt · Ochtend");
    }

    [Fact]
    public async Task Defaults_ToHomeOfficeAndToday_WithOverdueWindow()
    {
        using var client = CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/balie", Token));

        _fleet.Requests.ShouldContain(r => r.PathAndQuery == "/cars?office=utrecht");
        var schedule = _reservations.Requests.Single(r => r.Uri.AbsolutePath == "/schedule").Body!;
        schedule.ShouldContain("\"from\":\"2026-09-30\"");
        schedule.ShouldContain("\"to\":\"2026-10-21\"");
        html.ShouldContain("<option value=\"utrecht\" selected=\"selected\">Utrecht</option>");
        html.ShouldContain("name=\"datum\" type=\"date\" value=\"2026-10-14\"");
        html.ShouldContain("Woensdag 14 oktober 2026 · Utrecht");
    }

    [Fact]
    public async Task OtherDate_ShowsItsPickUpsAndStillTheOverdueCars()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync("/balie?vestiging=utrecht&datum=2026-10-15", Token);

        ItemNames().Matches(Section(html, "ophalen")).Select(m => m.Groups[1].Value).ShouldBe(["Noor"]);
        Item(html, RiskPeriod).ShouldContain("Uitgeven vanaf 08:00");
        ItemNames().Matches(Section(html, "te-laat")).Select(m => m.Groups[1].Value).ShouldBe(["Ilse"]);
        html.ShouldContain("<strong>0 uit, 0 terug, 1 te laat</strong>");
    }

    [Fact]
    public async Task DateOutOfRange_FallsBackToToday()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync("/balie?datum=2027-06-01", Token);

        html.ShouldContain("name=\"datum\" type=\"date\" value=\"2026-10-14\"");
    }

    [Fact]
    public async Task BoardHandler_ReturnsOnlyTheBoard()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/balie?handler=Board&vestiging=utrecht&datum=2026-10-14");
        request.Headers.Add("HX-Request", "true");

        using var response = await client.SendAsync(request, Token);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldNotContain("<html");
        html.ShouldStartWith("<section id=\"balie-board\"");
        html.ShouldContain("hx-get=\"/balie?handler=Board&vestiging=utrecht&datum=2026-10-14\"");
    }

    [Fact]
    public async Task PickUp_WithHtmx_HandsOutAsOperatorAndReturnsBoardWithToast()
    {
        using var client = CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);
        using var request = Post("PickUp", FleurPeriod, token);
        request.Headers.Add("HX-Request", "true");

        using var response = await client.SendAsync(request, Token);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var pickUp = _reservations.Requests
            .Where(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.EndsWith("/pickup", StringComparison.Ordinal))
            .ShouldHaveSingleItem();
        pickUp.Uri.AbsolutePath.ShouldBe($"/periods/{FleurPeriod}/pickup");
        pickUp.Headers[ActingUserHandler.OperatorHeader].ShouldBe("true");
        html.ShouldStartWith("<section id=\"balie-board\"");
        html.ShouldContain("hx-swap-oob=\"beforeend:#toasts\"");
        html.ShouldContain("Renault 5 (AA-11-BB) uitgegeven aan Fleur.");
    }

    [Fact]
    public async Task Return_WithoutJavaScript_RedirectsBackWithToast()
    {
        using var client = CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);
        using var request = Post("Return", IlsePeriod, token);

        using var response = await client.SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe("/balie?vestiging=utrecht&datum=2026-10-14");
        _reservations.Requests.ShouldContain(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath == $"/periods/{IlsePeriod}/return");
    }

    [Fact]
    public async Task FailedAction_ShowsTheProblemAsToast()
    {
        _reservations.On(
            HttpMethod.Post,
            $"/periods/{DaanPeriod}/pickup",
            _ => StubHttpHandler.Problem(HttpStatusCode.Forbidden, "not-allowed", "Je kunt een auto pas ophalen als je reservering begint."));
        using var client = CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);
        using var request = Post("PickUp", DaanPeriod, token);
        request.Headers.Add("HX-Request", "true");

        using var response = await client.SendAsync(request, Token);
        var html = await response.Content.ReadAsStringAsync(Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("toast-error");
        html.ShouldContain("Je kunt een auto pas ophalen als je reservering begint.");
    }

    [Fact]
    public async Task NonOperator_GetsDutchForbiddenPage()
    {
        using var client = CreateClient(roles: []);

        using var response = await client.GetAsync("/balie", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain("Geen toegang");
        _reservations.Requests.ShouldNotContain(r => r.Uri.AbsolutePath == "/schedule");
    }

    private static HttpRequestMessage Post(string handler, Guid periodId, string token) =>
        new(HttpMethod.Post, $"/balie?handler={handler}")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["id"] = periodId.ToString(),
                ["vestiging"] = "utrecht",
                ["datum"] = "2026-10-14",
            }),
        };

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/balie", Token);
        return TokenPattern().Match(html).Groups[1].Value;
    }

    private static string Section(string html, string name)
    {
        var match = Regex.Match(html, $"<section class=\"balie-column[^\"]*\" data-section=\"{name}\".*?</section>", RegexOptions.Singleline);
        match.Success.ShouldBeTrue($"No section {name}");
        return match.Value;
    }

    /// <summary>The item of a period, up to the next item or the end of its section.</summary>
    private static string Item(string html, Guid periodId)
    {
        var period = html.IndexOf($"data-period=\"{periodId}\"", StringComparison.Ordinal);
        period.ShouldBeGreaterThan(0, $"No item for {periodId}");
        var start = html.LastIndexOf("<li", period, StringComparison.Ordinal);
        var ends = new[] { "data-period=", "</section>" }
            .Select(marker => html.IndexOf(marker, period + 1, StringComparison.Ordinal))
            .Where(index => index > 0);
        return html[start..ends.Min()];
    }

    private static string Reservation(string carId) => $$"""
        {"id":"{{Guid.NewGuid()}}","employeeId":"{{Guid.NewGuid()}}","carId":"{{carId}}","from":"2026-10-14","to":"2026-10-14",
         "dayPart":"Morning","source":"User","createdAt":"2026-10-10T08:00:00+00:00","cancelledAt":null,
         "notPickedUp":false,"overdue":false,"periods":[]}
        """;

    // Real instants of the day parts in Amsterdam summer time
    private static string Entry(
        Guid periodId,
        string carId,
        string date,
        string dayPart,
        string status,
        string firstName,
        bool notPickedUp = false,
        bool overdue = false,
        bool atRisk = false)
    {
        var (start, end) = dayPart switch
        {
            "Morning" => ("06:00", "10:00"),
            "Afternoon" => ("10:00", "16:00"),
            _ => ("06:00", "16:00"),
        };
        return $$"""
            {"reservationId":"{{Guid.NewGuid()}}","periodId":"{{periodId}}","carId":"{{carId}}",
             "date":"{{date}}","dayPart":"{{dayPart}}","start":"{{date}}T{{start}}:00+00:00","end":"{{date}}T{{end}}:00+00:00",
             "status":"{{status}}","source":"User","employeeFirstName":"{{firstName}}",
             "notPickedUp":{{Json(notPickedUp)}},"overdue":{{Json(overdue)}},"atRisk":{{Json(atRisk)}}}
            """;
    }

    private static string Json(bool value) => value ? "true" : "false";

    [GeneratedRegex("<strong>([^<]+)</strong>\\s*<span>")]
    private static partial Regex ItemNames();

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    private HttpClient CreateClient(string[]? roles = null)
    {
        var customFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
            services.AddHttpClient<FleetApiClient>().ConfigurePrimaryHttpMessageHandler(() => _fleet);
            services.AddHttpClient<ReservationsApiClient>().ConfigurePrimaryHttpMessageHandler(() => _reservations);
        }));
        var client = customFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(
            EasyAuthAuthenticationDefaults.HeaderName,
            TestPrincipals.Header(TestPrincipals.KnownId, "Bram Testbeek", roles ?? [PoolCarRoles.FleetOperator]));
        return client;
    }
}
