using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PoolCar.Web.Auth;
using PoolCar.Web.Clients;
using PoolCar.Web.Tests.Support;
using Shouldly;

namespace PoolCar.Web.Tests;

public partial class PlanningPageTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string OfficesJson = """
        [{"id":"utrecht","name":"Utrecht","address":"Utrecht"},
         {"id":"amsterdam","name":"Amsterdam","address":"Amsterdam"}]
        """;

    private static readonly Guid MyReservationId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    // Start and end are deliberately unrelated to the day part, so placement can only come from date and day part.
    private static readonly string ScheduleJson = $"""
        [{Entry("AA11BB", "2026-10-15", "Morning", "Scheduled", "Fleur")},
         {Entry("AA11BB", "2026-10-16", "FullDay", "Scheduled", "Bram", reservationId: MyReservationId)},
         {Entry("CC22DD", "2026-10-14", "Morning", "PickedUp", "Daan")},
         {Entry("CC22DD", "2026-10-13", "Afternoon", "PickedUp", "Sanne", overdue: true)},
         {Entry("CC22DD", "2026-10-12", "Morning", "Returned", "Daan")},
         {Entry("CC22DD", "2026-10-17", "Morning", "Returned", "Daan")}]
        """;

    private readonly StubHttpHandler _fleet = FleetData.Fleet().OnJson(HttpMethod.Get, "/offices", OfficesJson);

    private readonly StubHttpHandler _reservations = new StubHttpHandler()
        .OnJson(
            HttpMethod.Get,
            $"/employees/{TestPrincipals.KnownId}",
            $$"""{"id":"{{TestPrincipals.KnownId}}","name":"Bram Testbeek","homeOffice":"utrecht","kind":"Seeded"}""")
        .OnJson(HttpMethod.Get, "/reservations/mine", $$"""
            [{"id":"{{MyReservationId}}","employeeId":"{{TestPrincipals.KnownId}}","carId":"AA11BB",
              "from":"2026-10-16","to":"2026-10-16","dayPart":"FullDay","source":"User",
              "createdAt":"2026-10-10T08:00:00+00:00","cancelledAt":null,"notPickedUp":false,"overdue":false,
              "periods":[]}]
            """)
        .OnJson(HttpMethod.Post, "/schedule", ScheduleJson);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("AA11BB", "2026-10-15", "ochtend", "gereserveerd")]
    [InlineData("AA11BB", "2026-10-15", "middag", "vrij")]
    [InlineData("AA11BB", "2026-10-16", "ochtend", "gereserveerd")]
    [InlineData("AA11BB", "2026-10-16", "middag", "gereserveerd")]
    [InlineData("CC22DD", "2026-10-14", "ochtend", "opgehaald")]
    [InlineData("CC22DD", "2026-10-14", "middag", "vrij")]
    [InlineData("CC22DD", "2026-10-13", "ochtend", "vrij")]
    [InlineData("CC22DD", "2026-10-13", "middag", "te-laat")]
    [InlineData("CC22DD", "2026-10-12", "ochtend", "ingeleverd")]
    [InlineData("CC22DD", "2026-10-17", "ochtend", "vrij")]
    public async Task Grid_PlacesPeriodsByDateAndDayPart(string plate, string date, string dayPart, string state)
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync("/planning", Token);

        Cell(html, plate, date, dayPart).ShouldContain($"data-state=\"{state}\"");
    }

    [Fact]
    public async Task Grid_MarksMyFullDayReservationInBothHalves()
    {
        using var client = CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/planning", Token));

        foreach (var half in new[] { "ochtend", "middag" })
        {
            var cell = Cell(html, "AA11BB", "2026-10-16", half);
            cell.ShouldContain("is-mine");
            cell.ShouldContain("is-full-day");
            cell.ShouldContain("data-mine=\"true\"");
            cell.ShouldContain("title=\"Bram (jij), vr 16 okt, hele dag 08:00–18:00\"");
            cell.ShouldContain("Gereserveerd door jou");
        }

        var other = Cell(html, "AA11BB", "2026-10-15", "ochtend");
        other.ShouldNotContain("is-mine");
        other.ShouldContain("data-mine=\"false\"");
        other.ShouldContain("title=\"Fleur, do 15 okt, ochtend 08:00–12:00\"");
        other.ShouldContain("Gereserveerd door Fleur");
        Cell(html, "CC22DD", "2026-10-13", "middag").ShouldContain("Te laat, nog bij Sanne");
    }

    [Fact]
    public async Task FreeCell_LinksToReservingThatCarDateAndDayPart()
    {
        using var client = CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/planning", Token));

        Cell(html, "AA11BB", "2026-10-15", "middag").ShouldContain(
            "href=\"/reserveren?auto=AA11BB&vestiging=utrecht&van=2026-10-15&tot=2026-10-15&dagdeel=middag\"");
        Cell(html, "CC22DD", "2026-10-18", "ochtend").ShouldContain(
            "href=\"/reserveren?auto=CC22DD&vestiging=utrecht&van=2026-10-18&tot=2026-10-18&dagdeel=ochtend\"");
        Cell(html, "AA11BB", "2026-10-15", "ochtend").ShouldNotContain("href=");
    }

    [Fact]
    public async Task PastHalfDays_AreNotClickable()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync("/planning", Token);

        var past = Cell(html, "AA11BB", "2026-10-13", "middag");
        past.ShouldContain("is-past");
        past.ShouldNotContain("href=");
        Cell(html, "AA11BB", "2026-10-14", "ochtend").ShouldNotContain("is-past");
        Cell(html, "AA11BB", "2026-10-14", "ochtend").ShouldContain("href=\"/reserveren");
    }

    [Fact]
    public async Task Afternoon_MakesTheMorningOfTodayPast()
    {
        using var client = CreateClient(now: new DateTimeOffset(2026, 10, 14, 13, 0, 0, TimeSpan.Zero));

        var html = await client.GetStringAsync("/planning", Token);

        Cell(html, "AA11BB", "2026-10-14", "ochtend").ShouldContain("is-past");
        Cell(html, "AA11BB", "2026-10-14", "middag").ShouldNotContain("is-past");
    }

    [Fact]
    public async Task Defaults_ToHomeOfficeFirstCategoryAndThisWeek()
    {
        using var client = CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/planning", Token));

        _fleet.Requests.ShouldContain(r => r.PathAndQuery == "/cars?office=utrecht&category=klein");
        ScheduleRange().ShouldBe(("2026-10-12", "2026-10-18"));
        html.ShouldContain("<option value=\"utrecht\" selected=\"selected\">Utrecht</option>");
        html.ShouldContain("<option value=\"klein\" selected=\"selected\">Klein</option>");
        html.ShouldContain("Week 42 · ma 12 – zo 18 okt");
        html.ShouldContain("<a href=\"/auto/AA11BB\">Renault 5</a>");
        html.ShouldContain("<span class=\"plate\">AA-11-BB</span>");
        html.ShouldContain("planning-legend");
    }

    [Fact]
    public async Task AllCategories_LoadsEveryCarOfTheOffice()
    {
        using var client = CreateClient();

        await client.GetStringAsync("/planning?vestiging=amsterdam&categorie=alle", Token);

        _fleet.Requests.ShouldContain(r => r.PathAndQuery == "/cars?office=amsterdam");
    }

    [Fact]
    public async Task Week_StartsOnMondayAndLinksToNeighbouringWeeks()
    {
        using var client = CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/planning?vestiging=utrecht&categorie=middel&week=2026-10-21", Token));

        ScheduleRange().ShouldBe(("2026-10-19", "2026-10-25"));
        html.ShouldContain("href=\"/planning?vestiging=utrecht&categorie=middel&week=2026-10-12\"");
        html.ShouldContain("href=\"/planning?vestiging=utrecht&categorie=middel&week=2026-10-26\"");
        PreviousLink().IsMatch(html).ShouldBeTrue();
        html.ShouldContain("name=\"week\" value=\"2026-10-19\"");
    }

    [Fact]
    public async Task InvalidWeek_FallsBackToThisWeek()
    {
        using var client = CreateClient();

        await client.GetStringAsync("/planning?week=volgende", Token);

        ScheduleRange().ShouldBe(("2026-10-12", "2026-10-18"));
    }

    [Fact]
    public async Task HtmxRequest_ReturnsBoardWithWeekForTheFilters()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/planning?week=2026-10-19");
        request.Headers.Add("HX-Request", "true");

        using var response = await client.SendAsync(request, Token);
        var html = await response.Content.ReadAsStringAsync(Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Vary.ShouldContain("HX-Request");
        html.ShouldNotContain("<html");
        html.ShouldContain("class=\"planning-grid\"");
        html.ShouldContain("id=\"planning-week\" name=\"week\" value=\"2026-10-19\" hx-swap-oob=\"true\"");
    }

    [Fact]
    public async Task EmptyCategory_SaysWhatToDo()
    {
        _fleet.OnJson(HttpMethod.Get, "/cars", "[]");
        using var client = CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/planning?categorie=luxe", Token));

        html.ShouldContain("Geen auto's in luxe in Utrecht.");
        html.ShouldContain("Kies een andere categorie of vestiging.");
    }

    private (string From, string To) ScheduleRange()
    {
        using var body = JsonDocument.Parse(_reservations.Requests.Single(r => r.Uri.AbsolutePath == "/schedule").Body!);
        return (body.RootElement.GetProperty("from").GetString()!, body.RootElement.GetProperty("to").GetString()!);
    }

    /// <summary>The cell of a car for a date and day part, from its opening tag to its closing tag.</summary>
    private static string Cell(string html, string plate, string date, string dayPart)
    {
        var rowStart = html.IndexOf($"href=\"/auto/{plate}\"", StringComparison.Ordinal);
        rowStart.ShouldBeGreaterThan(0, $"No row for {plate}");
        var rowEnd = html.IndexOf("</tr>", rowStart, StringComparison.Ordinal);
        var row = html[rowStart..rowEnd];
        var match = Regex.Match(
            row,
            $"<td[^>]*data-date=\"{date}\"[^>]*data-dagdeel=\"{dayPart}\"[^>]*>.*?</td>",
            RegexOptions.Singleline);
        match.Success.ShouldBeTrue($"No cell for {plate} on {date} {dayPart}");
        return match.Value;
    }

    private static string Entry(
        string carId,
        string date,
        string dayPart,
        string status,
        string firstName,
        bool overdue = false,
        Guid? reservationId = null) => $$"""
        {"reservationId":"{{reservationId ?? Guid.NewGuid()}}","periodId":"{{Guid.NewGuid()}}","carId":"{{carId}}",
         "date":"{{date}}","dayPart":"{{dayPart}}","start":"2026-10-01T00:00:00+00:00","end":"2026-10-01T00:30:00+00:00",
         "status":"{{status}}","source":"User","employeeFirstName":"{{firstName}}","notPickedUp":false,
         "overdue":{{(overdue ? "true" : "false")}},"atRisk":false}
        """;

    [GeneratedRegex("<a [^>]*href=\"/planning\\?vestiging=utrecht&categorie=middel&week=2026-10-12\"[^>]*>Vorige</a>")]
    private static partial Regex PreviousLink();

    // Wednesday 14 October, in the morning in Amsterdam whatever the server's time zone
    private HttpClient CreateClient(DateTimeOffset? now = null)
    {
        var customFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(now ?? new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero)));
            services.AddHttpClient<FleetApiClient>().ConfigurePrimaryHttpMessageHandler(() => _fleet);
            services.AddHttpClient<ReservationsApiClient>().ConfigurePrimaryHttpMessageHandler(() => _reservations);
        }));
        var client = customFactory.CreateClient();
        client.DefaultRequestHeaders.Add(
            EasyAuthAuthenticationDefaults.HeaderName,
            TestPrincipals.Header(TestPrincipals.KnownId, "Bram Testbeek", []));
        return client;
    }
}
