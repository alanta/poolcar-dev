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

public partial class MyReservationsPageTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ReservationId = "11111111-1111-1111-1111-111111111111";
    private const string PeriodId = "22222222-2222-2222-2222-222222222222";

    private const string CarsJson = """
        [{"plate":"KSP73B","make":"Skoda","model":"Elroq","variant":"85","category":"middel","office":"utrecht",
          "colour":"Groen","seats":5,"rangeKm":560,"powerKw":210,"firstRegistration":"2025-03-01","listPrice":null}]
        """;

    // 11:00 in Amsterdam on Tuesday 13 October 2026
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly StubHttpHandler _reservations = new StubHttpHandler()
        .OnJson(
            HttpMethod.Get,
            $"/employees/{TestPrincipals.KnownId}",
            $$"""{"id":"{{TestPrincipals.KnownId}}","name":"Bram Testbeek","homeOffice":"utrecht","kind":"Seeded"}""");

    private readonly StubHttpHandler _fleet = new StubHttpHandler().OnJson(HttpMethod.Get, "/cars", CarsJson);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutReservations_EveryGroupSaysWhatToDo()
    {
        _reservations.OnJson(HttpMethod.Get, "/reservations/mine", "[]");
        using var client = CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/mijn-reserveringen", Token));

        html.ShouldContain("<h2 id=\"onderweg\">Onderweg</h2>");
        html.ShouldContain("<h2 id=\"gepland\">Gepland</h2>");
        html.ShouldContain("<h2 id=\"afgelopen\">Afgelopen</h2>");
        html.ShouldContain("Je bent nu niet onderweg.");
        html.ShouldContain("<a class=\"button\" href=\"/nu\">Nu een auto</a>");
        html.ShouldContain("Je hebt niets gepland.");
        html.ShouldContain("<a class=\"button\" href=\"/reserveren\">Auto reserveren</a>");
        html.ShouldContain("Je hebt de laatste 30 dagen geen auto gebruikt.");
        _fleet.Requests.ShouldNotContain(r => r.Uri.AbsolutePath == "/cars");
    }

    [Fact]
    public async Task ShowsCardsPerGroup_WithActions()
    {
        _reservations.OnJson(
            HttpMethod.Get,
            "/reservations/mine",
            $"[{ReservationJson(ReservationId, PeriodId, "Scheduled", Now.AddHours(-1), Now.AddHours(1))}," +
            $"{ReservationJson("33333333-3333-3333-3333-333333333333", "44444444-4444-4444-4444-444444444444", "PickedUp", Now.AddHours(-3), Now.AddHours(-1), overdue: true)}]");
        using var client = CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/mijn-reserveringen", Token));

        html.ShouldContain("Di 13 okt, ochtend");
        html.ShouldContain("Skoda Elroq <span class=\"plate\">KSP-73-B</span>");
        html.ShouldContain($"hx-post=\"/mijn-reserveringen?id={ReservationId}&periodId={PeriodId}&handler=PickUp\"");
        html.ShouldContain("Ophalen");
        html.ShouldContain("Inleveren");
        html.ShouldContain("Te laat sinds 10:00");
        html.ShouldContain($"hx-get=\"/mijn-reserveringen?id={ReservationId}&handler=Cancel\"");
        html.ShouldNotContain("Weet je het zeker?");
        html.IndexOf("Inleveren", StringComparison.Ordinal)
            .ShouldBeLessThan(html.IndexOf("Ophalen", StringComparison.Ordinal));
        _fleet.Requests.ShouldContain(r => r.PathAndQuery == "/cars?plates=KSP73B");
    }

    [Fact]
    public async Task Cancel_WithHtmx_SwapsInTheConfirmation()
    {
        StubReservation();
        using var client = CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/mijn-reserveringen?handler=Cancel&id={ReservationId}");
        request.Headers.Add("HX-Request", "true");
        using var response = await client.SendAsync(request, Token);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldNotContain("<html");
        html.ShouldStartWith($"<article id=\"reservering-{ReservationId}\"");
        html.ShouldContain("Weet je het zeker?");
        html.ShouldContain("Ja <span class=\"spinner\"");
        html.ShouldContain($"hx-get=\"/mijn-reserveringen?id={ReservationId}&handler=Card\"");
        html.ShouldContain(">Nee</a>");
    }

    [Fact]
    public async Task Cancel_WithoutJavaScript_ShowsThePageWithTheConfirmation()
    {
        _reservations.OnJson(
            HttpMethod.Get,
            "/reservations/mine",
            $"[{ReservationJson(ReservationId, PeriodId, "Scheduled", Now.AddHours(2), Now.AddHours(6))}]");
        using var client = CreateClient();

        var html = WebUtility.HtmlDecode(
            await client.GetStringAsync($"/mijn-reserveringen?handler=Cancel&id={ReservationId}", Token));

        html.ShouldContain("<h1>Mijn reserveringen</h1>");
        html.ShouldContain("Weet je het zeker?");
        html.ShouldContain($"action=\"/mijn-reserveringen?id={ReservationId}&handler=Cancel\"");
        html.ShouldContain("name=\"__RequestVerificationToken\"");
    }

    [Fact]
    public async Task No_WithHtmx_SwapsBackTheCard()
    {
        StubReservation();
        using var client = CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/mijn-reserveringen?handler=Card&id={ReservationId}");
        request.Headers.Add("HX-Request", "true");
        using var response = await client.SendAsync(request, Token);
        var html = await response.Content.ReadAsStringAsync(Token);

        html.ShouldNotContain("Weet je het zeker?");
        html.ShouldContain(">Annuleren</a>");
    }

    [Fact]
    public async Task ConfirmCancel_WithHtmx_SwapsTheCardAndShowsToast()
    {
        _reservations.OnJson(
            HttpMethod.Post,
            $"/reservations/{ReservationId}/cancel",
            ReservationJson(ReservationId, PeriodId, "Cancelled", Now.AddHours(2), Now.AddHours(6)));
        using var client = CreateClient();

        using var response = await PostAsync(client, $"/mijn-reserveringen?handler=Cancel&id={ReservationId}", htmx: true);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldStartWith($"<article id=\"reservering-{ReservationId}\"");
        html.ShouldContain("Geannuleerd");
        html.ShouldNotContain(">Annuleren</a>");
        html.ShouldContain("hx-swap-oob=\"beforeend:#toasts\"");
        html.ShouldContain("Je reservering van de Skoda Elroq KSP-73-B is geannuleerd.");
    }

    [Fact]
    public async Task ConfirmCancel_WithoutJavaScript_RedirectsAndShowsToast()
    {
        _reservations.OnJson(
            HttpMethod.Post,
            $"/reservations/{ReservationId}/cancel",
            ReservationJson(ReservationId, PeriodId, "Cancelled", Now.AddHours(2), Now.AddHours(6)));
        _reservations.OnJson(HttpMethod.Get, "/reservations/mine", "[]");
        using var client = CreateClient();

        using var response = await PostAsync(client, $"/mijn-reserveringen?handler=Cancel&id={ReservationId}", htmx: false);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe($"/mijn-reserveringen#reservering-{ReservationId}");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/mijn-reserveringen", Token));
        html.ShouldContain("Je reservering van de Skoda Elroq KSP-73-B is geannuleerd.");
    }

    [Fact]
    public async Task PickUpConflict_WithHtmx_KeepsTheCardAndShowsTheDetail()
    {
        StubReservation();
        _reservations.On(
            HttpMethod.Post,
            $"/periods/{PeriodId}/pickup",
            _ => StubHttpHandler.Problem(HttpStatusCode.Conflict, "conflict", "Deze auto is nog niet terug."));
        using var client = CreateClient();

        using var response = await PostAsync(
            client,
            $"/mijn-reserveringen?handler=PickUp&id={ReservationId}&periodId={PeriodId}",
            htmx: true);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        html.ShouldStartWith($"<article id=\"reservering-{ReservationId}\"");
        html.ShouldContain("Ophalen");
        html.ShouldContain("toast-error");
        html.ShouldContain("Deze auto is nog niet terug.");
    }

    [Fact]
    public async Task ReturnForbidden_WithHtmx_ShowsOnlyTheErrorToast_WhenTheReservationIsGone()
    {
        _reservations.On(
            HttpMethod.Post,
            $"/periods/{PeriodId}/return",
            _ => StubHttpHandler.Problem(HttpStatusCode.Forbidden, "not-allowed", "Dit is niet jouw reservering."));
        using var client = CreateClient();

        using var response = await PostAsync(
            client,
            $"/mijn-reserveringen?handler=Return&id={ReservationId}&periodId={PeriodId}",
            htmx: true);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("HX-Reswap").ShouldHaveSingleItem().ShouldBe("none");
        html.ShouldNotContain("<article");
        html.ShouldContain("Dit is niet jouw reservering.");
    }

    [Fact]
    public async Task Return_WithHtmx_ShowsThankYouToast()
    {
        _reservations.OnJson(
            HttpMethod.Post,
            $"/periods/{PeriodId}/return",
            ReservationJson(ReservationId, PeriodId, "Returned", Now.AddHours(-2), Now));
        using var client = CreateClient();

        using var response = await PostAsync(
            client,
            $"/mijn-reserveringen?handler=Return&id={ReservationId}&periodId={PeriodId}",
            htmx: true);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));

        html.ShouldContain("Bedankt! Je hebt de Skoda Elroq KSP-73-B ingeleverd.");
        html.ShouldNotContain("Inleveren");
    }

    private void StubReservation() => _reservations.OnJson(
        HttpMethod.Get,
        $"/reservations/{ReservationId}",
        ReservationJson(ReservationId, PeriodId, "Scheduled", Now.AddHours(-1), Now.AddHours(1)));

    private static string ReservationJson(
        string id,
        string periodId,
        string status,
        DateTimeOffset start,
        DateTimeOffset end,
        bool overdue = false) => $$"""
        {"id":"{{id}}","employeeId":"{{TestPrincipals.KnownId}}","carId":"KSP73B","from":"2026-10-13",
         "to":"2026-10-13","dayPart":"Morning","source":"User","createdAt":"2026-10-10T08:00:00+00:00",
         "cancelledAt":null,"notPickedUp":false,"overdue":{{(overdue ? "true" : "false")}},
         "periods":[{"id":"{{periodId}}","date":"2026-10-13","start":"{{start:O}}","end":"{{end:O}}",
           "status":"{{status}}","pickedUpAt":null,"returnedAt":null,"notPickedUp":false,
           "overdue":{{(overdue ? "true" : "false")}}}]}
        """;

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, bool htmx)
    {
        var page = await client.GetStringAsync("/thuislocatie", Token);
        var token = TokenPattern().Match(page).Groups[1].Value;
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }),
        };
        if (htmx)
        {
            request.Headers.Add("HX-Request", "true");
        }

        return await client.SendAsync(request, Token);
    }

    private HttpClient CreateClient()
    {
        _fleet.OnJson(HttpMethod.Get, "/offices", """[{"id":"utrecht","name":"Utrecht","address":"Utrecht"}]""");
        var customFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
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
