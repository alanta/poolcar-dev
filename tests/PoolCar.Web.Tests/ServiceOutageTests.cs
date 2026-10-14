using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using PoolCar.Web.Auth;
using PoolCar.Web.Clients;
using PoolCar.Web.Outages;
using PoolCar.Web.Tests.Support;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Shouldly;

namespace PoolCar.Web.Tests;

public partial class ServiceOutageTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ReservationsUnavailable =
        "Reserveringen zijn op dit moment niet beschikbaar. Probeer het later opnieuw.";

    private const string FleetUnavailable = "Het wagenpark is op dit moment niet beschikbaar. Probeer het later opnieuw.";

    private const string PeriodId = "22222222-2222-2222-2222-222222222222";

    private readonly StubHttpHandler _fleet = FleetData.Fleet()
        .OnJson(HttpMethod.Get, "/offices", """[{"id":"utrecht","name":"Utrecht","address":"Utrecht"}]""");

    private readonly StubHttpHandler _reservations = new StubHttpHandler()
        .OnJson(
            HttpMethod.Get,
            $"/employees/{TestPrincipals.KnownId}",
            $$"""{"id":"{{TestPrincipals.KnownId}}","name":"Bram Testbeek","homeOffice":"utrecht","kind":"Seeded"}""")
        .OnJson(HttpMethod.Get, "/reservations/mine", "[]");

    private readonly CapturingLoggerProvider _logs = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> Outages => ["connection", "server-error", "timeout", "circuit-open"];

    [Theory]
    [MemberData(nameof(Outages))]
    public async Task ReservationsDown_ShowsUnavailablePage(string outage)
    {
        using var client = CreateClient(reservations: Failing(outage));

        using var response = await client.GetAsync("/mijn-reserveringen", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.GetValues(ServiceUnavailableMiddleware.MarkerHeader).ShouldHaveSingleItem().ShouldBe("true");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
        html.ShouldContain("<h1>Tijdelijk niet beschikbaar</h1>");
        html.ShouldContain(ReservationsUnavailable);
        html.ShouldContain("class=\"site-header\"");
        html.ShouldContain("Bram Testbeek");
        html.ShouldContain("class=\"trace-id\"");
    }

    [Fact]
    public async Task ReservationsDown_WithHtmx_ShowsOnlyAToast()
    {
        using var client = CreateClient(reservations: Failing("connection"));

        using var response = await client.SendAsync(HtmxGet("/mijn-reserveringen"), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.GetValues("HX-Reswap").ShouldHaveSingleItem().ShouldBe("none");
        response.Headers.GetValues(ServiceUnavailableMiddleware.MarkerHeader).ShouldHaveSingleItem().ShouldBe("true");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
        html.ShouldNotContain("<html");
        html.ShouldContain("hx-swap-oob=\"beforeend:#toasts\"");
        html.ShouldContain("toast-error");
        html.ShouldContain(ReservationsUnavailable);
    }

    [Fact]
    public async Task ReservationsWriteFails_WithHtmx_ShowsOnlyAToast()
    {
        _reservations.On(
            HttpMethod.Post,
            $"/periods/{PeriodId}/return",
            _ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        using var client = CreateClient();

        using var response = await PostAsync(
            client,
            $"/mijn-reserveringen?handler=Return&id=11111111-1111-1111-1111-111111111111&periodId={PeriodId}",
            htmx: true);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
        html.ShouldNotContain("<html");
        html.ShouldContain(ReservationsUnavailable);
    }

    [Fact]
    public async Task ReservationsWriteFails_WithoutJavaScript_ShowsUnavailablePage()
    {
        _reservations.On(
            HttpMethod.Post,
            $"/periods/{PeriodId}/return",
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = CreateClient();

        using var response = await PostAsync(
            client,
            $"/mijn-reserveringen?handler=Return&id=11111111-1111-1111-1111-111111111111&periodId={PeriodId}",
            htmx: false);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
        html.ShouldContain("<html");
        html.ShouldContain(ReservationsUnavailable);
    }

    [Theory]
    [MemberData(nameof(Outages))]
    public async Task FleetDown_ShowsUnavailablePage(string outage)
    {
        using var client = CreateClient(fleet: Failing(outage));

        using var response = await client.GetAsync("/wagenpark", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.GetValues(ServiceUnavailableMiddleware.MarkerHeader).ShouldHaveSingleItem().ShouldBe("true");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
        html.ShouldContain("<h1>Tijdelijk niet beschikbaar</h1>");
        html.ShouldContain(FleetUnavailable);
        html.ShouldContain("class=\"site-header\"");
    }

    [Fact]
    public async Task FleetDown_WithHtmx_ShowsOnlyAToast()
    {
        using var client = CreateClient(fleet: Failing("connection"));

        using var response = await client.SendAsync(HtmxGet("/wagenpark?categorie=klein"), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.GetValues("HX-Reswap").ShouldHaveSingleItem().ShouldBe("none");
        response.Headers.GetValues("HX-Push-Url").ShouldHaveSingleItem().ShouldBe("false");
        response.Headers.GetValues(ServiceUnavailableMiddleware.MarkerHeader).ShouldHaveSingleItem().ShouldBe("true");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
        html.ShouldNotContain("<html");
        html.ShouldContain(FleetUnavailable);
    }

    [Fact]
    public async Task FleetDown_InProduction_ShowsUnavailablePage_AndLogsOnce()
    {
        using var client = CreateClient(fleet: Failing("connection"), environment: "Production");

        using var response = await client.GetAsync("/wagenpark", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain(FleetUnavailable);
        var entry = _logs.Entries.Where(e => e.Category == typeof(ServiceUnavailableMiddleware).FullName)
            .ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe(
            "The Fleet API is unavailable for GET /wagenpark: HttpRequestException: The SSL connection could not be established.");
        _logs.Entries.ShouldNotContain(e => e.Level >= LogLevel.Error && e.Category != "Polly");
    }

    [Fact]
    public async Task UnavailablePage_WithoutKnownService_ShowsGeneralMessage()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/fout/503", Token);

        (await response.Content.ReadAsStringAsync(Token))
            .ShouldContain("PoolCar is op dit moment niet beschikbaar. Probeer het later opnieuw.");
    }

    [Fact]
    public async Task Htmx_SwapsOnlyMarked503Responses()
    {
        using var client = CreateClient();

        var script = await client.GetStringAsync("/js/site.js", Token);

        script.ShouldNotContain("code: \"503\"");
        script.ShouldContain($"xhr.getResponseHeader(\"{ServiceUnavailableMiddleware.MarkerHeader}\") === \"true\"");
    }

    [Fact]
    public async Task UnknownEmployee_StillGoesToWelcomePage()
    {
        _reservations.On(
            HttpMethod.Get,
            $"/employees/{TestPrincipals.KnownId}",
            _ => StubHttpHandler.Problem(HttpStatusCode.NotFound, "not-found", "Niet gevonden."));
        using var client = CreateClient();

        using var response = await client.GetAsync("/wagenpark", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldStartWith("/welkom");
    }

    [Fact]
    public async Task UnknownCar_StillShowsNotFoundPage()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/auto/ZZ99ZZ", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.Contains(ServiceUnavailableMiddleware.MarkerHeader).ShouldBeFalse();
        (await response.Content.ReadAsStringAsync(Token)).ShouldNotContain("niet beschikbaar");
    }

    [Fact]
    public async Task ReservationsConflict_WithHtmx_StillShowsTheDetail()
    {
        _reservations.On(
            HttpMethod.Post,
            $"/periods/{PeriodId}/return",
            _ => StubHttpHandler.Problem(HttpStatusCode.Forbidden, "not-allowed", "Dit is niet jouw reservering."));
        using var client = CreateClient();

        using var response = await PostAsync(
            client,
            $"/mijn-reserveringen?handler=Return&id=11111111-1111-1111-1111-111111111111&periodId={PeriodId}",
            htmx: true);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
        html.ShouldContain("Dit is niet jouw reservering.");
        html.ShouldNotContain("niet beschikbaar");
    }

    [Fact]
    public async Task FleetClientError_IsNotAnOutage()
    {
        _fleet.On(HttpMethod.Get, "/cars", _ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        using var client = CreateClient();

        using var response = await client.GetAsync("/wagenpark", Token);

        response.StatusCode.ShouldNotBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.Contains(ServiceUnavailableMiddleware.MarkerHeader).ShouldBeFalse();
        _logs.Entries.ShouldNotContain(e => e.Category == typeof(ServiceUnavailableMiddleware).FullName);
    }

    private static HttpMessageHandler Failing(string outage) => new FailingHandler(outage switch
    {
        "connection" => () => throw new HttpRequestException(
            HttpRequestError.SecureConnectionError,
            "The SSL connection could not be established."),
        "server-error" => () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
        "timeout" => () => throw new TimeoutRejectedException(),
        "circuit-open" => () => throw new BrokenCircuitException(),
        _ => throw new ArgumentOutOfRangeException(nameof(outage)),
    });

    private static HttpRequestMessage HtmxGet(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("HX-Request", "true");
        return request;
    }

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

    private HttpClient CreateClient(
        HttpMessageHandler? fleet = null,
        HttpMessageHandler? reservations = null,
        string? environment = null)
    {
        var customFactory = factory.WithWebHostBuilder(builder =>
        {
            if (environment is not null)
            {
                builder.UseEnvironment(environment);
            }

            builder.ConfigureLogging(logging => logging.AddProvider(_logs));
            builder.ConfigureServices(services =>
            {
                services.ConfigureAll<HttpStandardResilienceOptions>(options => options.Retry.Delay = TimeSpan.Zero);
                services.AddHttpClient<FleetApiClient>().ConfigurePrimaryHttpMessageHandler(() => fleet ?? _fleet);
                services.AddHttpClient<ReservationsApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => reservations ?? _reservations);
            });
        });
        var client = customFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(
            EasyAuthAuthenticationDefaults.HeaderName,
            TestPrincipals.Header(TestPrincipals.KnownId, "Bram Testbeek"));
        return client;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    private sealed class FailingHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond());
    }

    private sealed record LogEntry(string Category, LogLevel Level, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => [.. _entries];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception)));
        }
    }
}
