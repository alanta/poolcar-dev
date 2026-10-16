using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoolCar.Web.Auth;
using PoolCar.Web.Clients;
using PoolCar.Web.ExpiredForms;
using PoolCar.Web.Tests.Support;
using Shouldly;

namespace PoolCar.Web.Tests;

public partial class ExpiredFormTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ExpiredMessage = "Deze pagina is verlopen. Laad de pagina opnieuw en probeer het nog eens.";

    private readonly StubHttpHandler _fleet = FleetData.Fleet()
        .OnJson(HttpMethod.Get, "/offices", """[{"id":"utrecht","name":"Utrecht","address":"Utrecht"}]""");

    private readonly StubHttpHandler _reservations = new StubHttpHandler()
        .OnJson(
            HttpMethod.Get,
            $"/employees/{TestPrincipals.KnownId}",
            $$"""{"id":"{{TestPrincipals.KnownId}}","name":"Bram Testbeek","homeOffice":"utrecht","kind":"Seeded"}""");

    private readonly CapturingLoggerProvider _logs = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MissingToken_WithoutJavaScript_ShowsExpiredPage()
    {
        using var client = CreateClient();
        using var request = Post("/thuislocatie?returnUrl=%2Fwagenpark", token: null, referrer: "http://localhost/thuislocatie?returnUrl=%2Fwagenpark");

        using var response = await client.SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Headers.GetValues(ExpiredFormFilter.MarkerHeader).ShouldHaveSingleItem().ShouldBe("true");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
        html.ShouldContain("<h1>Pagina verlopen</h1>");
        html.ShouldContain(ExpiredMessage);
        html.ShouldContain("class=\"site-header\"");
        html.ShouldContain("href=\"/thuislocatie?returnUrl=%2Fwagenpark\" hx-boost=\"false\">Pagina opnieuw laden</a>");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://example.com/thuislocatie")]
    [InlineData("http://localhost//example.com/thuislocatie")]
    [InlineData("/thuislocatie")]
    public async Task InvalidToken_WithoutLocalReferrer_ReloadsTheHomePage(string? referrer)
    {
        using var client = CreateClient();
        using var request = Post("/thuislocatie", token: "verlopen", referrer);

        using var response = await client.SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
        html.ShouldContain(ExpiredMessage);
        html.ShouldContain("href=\"/\" hx-boost=\"false\">Pagina opnieuw laden</a>");
    }

    [Fact]
    public async Task InvalidToken_BehindProxy_ReloadsTheSameOriginReferrer()
    {
        using var client = CreateClient();
        using var request = Post("/thuislocatie", token: "verlopen", referrer: "https://poolcar.example.com/thuislocatie");
        request.Headers.Add("Sec-Fetch-Site", "same-origin");

        using var response = await client.SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
        html.ShouldContain("href=\"/thuislocatie\" hx-boost=\"false\">Pagina opnieuw laden</a>");
    }

    [Fact]
    public async Task InvalidToken_WithHtmx_ShowsOnlyAToastWithReloadLink()
    {
        using var client = CreateClient();
        using var request = Post("/thuislocatie", token: "verlopen", referrer: "http://localhost/thuislocatie");
        request.Headers.Add("HX-Request", "true");

        using var response = await client.SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Headers.GetValues(ExpiredFormFilter.MarkerHeader).ShouldHaveSingleItem().ShouldBe("true");
        response.Headers.GetValues("HX-Reswap").ShouldHaveSingleItem().ShouldBe("none");
        response.Headers.GetValues("HX-Push-Url").ShouldHaveSingleItem().ShouldBe("false");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
        html.ShouldNotContain("<html");
        html.ShouldContain("hx-swap-oob=\"beforeend:#toasts\"");
        html.ShouldContain("toast-error");
        html.ShouldContain("toast-sticky");
        html.ShouldContain(ExpiredMessage);
        html.ShouldContain("<a class=\"toast-link\" href=\"/thuislocatie\" hx-boost=\"false\">Pagina opnieuw laden</a>");
    }

    [Fact]
    public async Task InvalidToken_WithBoostedForm_ShowsExpiredPage()
    {
        using var client = CreateClient();
        using var request = Post("/thuislocatie", token: "verlopen", referrer: "http://localhost/thuislocatie");
        request.Headers.Add("HX-Request", "true");
        request.Headers.Add("HX-Boosted", "true");

        using var response = await client.SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Headers.GetValues(ExpiredFormFilter.MarkerHeader).ShouldHaveSingleItem().ShouldBe("true");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
        html.ShouldContain("<h1>Pagina verlopen</h1>");
        html.ShouldContain("href=\"/thuislocatie\" hx-boost=\"false\">Pagina opnieuw laden</a>");
    }

    [Fact]
    public async Task TokenFromBeforeRestart_ShowsExpiredPage_AndLogsAWarning()
    {
        var (cookie, token) = await GetTokensAsync(CreateFactory(environment: "Production"));
        _logs.Clear();
        using var client = CreateFactory(environment: "Production").CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        SignIn(client);
        using var request = Post("/thuislocatie", token, referrer: "http://localhost/thuislocatie");
        request.Headers.Add("Cookie", cookie);

        using var response = await client.SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain("Pagina verlopen");
        var entry = _logs.Entries.Where(e => e.Category == typeof(ExpiredFormFilter).FullName).ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe(
            "The antiforgery token for POST /thuislocatie is missing or invalid, so the page is shown as expired. Htmx: False, antiforgery cookie present: True");
        _logs.Entries.ShouldNotContain(e => e.Level >= LogLevel.Error || e.HasException);
    }

    [Fact]
    public async Task ValidToken_IsNotAffected()
    {
        var customFactory = CreateFactory();
        using var client = customFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        SignIn(client);
        var page = await client.GetStringAsync("/thuislocatie", Token);
        using var request = Post("/thuislocatie", TokenPattern().Match(page).Groups[1].Value, referrer: null);

        using var response = await client.SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains(ExpiredFormFilter.MarkerHeader).ShouldBeFalse();
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
        html.ShouldContain("Kies een vestiging uit de lijst.");
        html.ShouldNotContain(ExpiredMessage);
        _logs.Entries.ShouldNotContain(e => e.Category == typeof(ExpiredFormFilter).FullName);
    }

    [Fact]
    public async Task OtherBadRequest_IsNotMarkedAsExpired()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/fout/400", Token);

        response.Headers.Contains(ExpiredFormFilter.MarkerHeader).ShouldBeFalse();
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
        html.ShouldContain("<h1>Dat lukte niet</h1>");
        html.ShouldNotContain(ExpiredMessage);
    }

    [Fact]
    public async Task Htmx_SwapsOnlyMarked400Responses()
    {
        using var client = CreateClient();

        var script = await client.GetStringAsync("/js/site.js", Token);

        script.ShouldNotContain("code: \"400\"");
        script.ShouldContain(
            $"xhr.status === 400 && xhr.getResponseHeader(\"{ExpiredFormFilter.MarkerHeader}\") === \"true\"");
    }

    private static HttpRequestMessage Post(string url, string? token, string? referrer)
    {
        var form = new Dictionary<string, string> { ["Office"] = "onbekend" };
        if (token is not null)
        {
            form["__RequestVerificationToken"] = token;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        if (referrer is not null)
        {
            request.Headers.TryAddWithoutValidation("Referer", referrer);
        }

        return request;
    }

    private static async Task<(string Cookie, string Token)> GetTokensAsync(WebApplicationFactory<Program> before)
    {
        using var client = before.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        SignIn(client);
        using var response = await client.GetAsync("/thuislocatie", Token);
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal))
            .Split(';')[0];
        var page = await response.Content.ReadAsStringAsync(Token);
        return (cookie, TokenPattern().Match(page).Groups[1].Value);
    }

    private HttpClient CreateClient()
    {
        var client = CreateFactory().CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        SignIn(client);
        return client;
    }

    private static void SignIn(HttpClient client) => client.DefaultRequestHeaders.Add(
        EasyAuthAuthenticationDefaults.HeaderName,
        TestPrincipals.Header(TestPrincipals.KnownId, "Bram Testbeek"));

    // Each factory has its own keys, like the app after a restart
    private WebApplicationFactory<Program> CreateFactory(string? environment = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            if (environment is not null)
            {
                builder.UseEnvironment(environment);
            }

            builder.ConfigureLogging(logging => logging.AddProvider(_logs));
            builder.ConfigureServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddHttpClient<FleetApiClient>().ConfigurePrimaryHttpMessageHandler(() => _fleet);
                services.AddHttpClient<ReservationsApiClient>().ConfigurePrimaryHttpMessageHandler(() => _reservations);
            });
        });

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    private sealed record LogEntry(string Category, LogLevel Level, string Message, bool HasException);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => [.. _entries];

        public void Clear() => _entries.Clear();

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
                entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception), exception is not null));
        }
    }
}
