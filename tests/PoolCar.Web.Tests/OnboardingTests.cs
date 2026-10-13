using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PoolCar.Web.Auth;
using PoolCar.Web.Clients;
using PoolCar.Web.Onboarding;
using PoolCar.Web.Tests.Support;
using Shouldly;

namespace PoolCar.Web.Tests;

public partial class OnboardingTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string OfficesJson = """
        [{"id":"utrecht","name":"Utrecht","address":"Utrecht"},
         {"id":"amsterdam","name":"Amsterdam","address":"Amsterdam"},
         {"id":"eindhoven","name":"Eindhoven","address":"Eindhoven"}]
        """;

    private readonly StubHttpHandler _fleet = new StubHttpHandler()
        .OnJson(HttpMethod.Get, "/offices", OfficesJson)
        .OnJson(HttpMethod.Get, "/categories", "[]")
        .OnJson(HttpMethod.Get, "/cars", "[]");

    private readonly StubHttpHandler _reservations = new StubHttpHandler()
        .OnJson(HttpMethod.Get, $"/employees/{TestPrincipals.KnownId}", EmployeeJson(TestPrincipals.KnownId, "Bram Testbeek"))
        .On(HttpMethod.Put, $"/employees/{TestPrincipals.UnknownId}", _ => StubHttpHandler.Json(
            EmployeeJson(TestPrincipals.UnknownId, "Jan de Boer")));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/", "/welkom?returnUrl=%2F")]
    [InlineData("/wagenpark?vestiging=amsterdam", "/welkom?returnUrl=%2Fwagenpark%3Fvestiging%3Damsterdam")]
    public async Task UnknownEmployee_IsSentToWelcomePage(string path, string expectedLocation)
    {
        using var client = CreateClient(TestPrincipals.UnknownId);

        using var response = await client.GetAsync(path, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe(expectedLocation);
    }

    [Fact]
    public async Task KnownEmployee_SeesRequestedPage()
    {
        using var client = CreateClient(TestPrincipals.KnownId);

        using var response = await client.GetAsync("/wagenpark", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WelcomePage_GreetsByFirstName_AndShowsOffices()
    {
        using var client = CreateClient(TestPrincipals.UnknownId);

        using var response = await client.GetAsync("/welkom?returnUrl=%2Fwagenpark", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync(Token);
        html.ShouldContain("Welkom bij PoolCar, Jan.");
        html.ShouldContain("value=\"Jan de Boer\"");
        html.ShouldContain(">Utrecht</button>");
        html.ShouldContain(">Amsterdam</button>");
        html.ShouldContain(">Eindhoven</button>");
    }

    [Fact]
    public async Task WelcomePage_ForKnownEmployee_RedirectsBack()
    {
        using var client = CreateClient(TestPrincipals.KnownId);

        using var response = await client.GetAsync("/welkom?returnUrl=%2Fwagenpark", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe("/wagenpark");
    }

    [Theory]
    [InlineData("/wagenpark?vestiging=amsterdam", "/wagenpark?vestiging=amsterdam")]
    [InlineData("https://example.test/", "/")]
    [InlineData("//example.test/", "/")]
    [InlineData("", "/")]
    public async Task PostingOffice_SavesEmployee_AndRedirectsToLocalReturnUrl(string returnUrl, string expectedLocation)
    {
        using var client = CreateClient(TestPrincipals.UnknownId);
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await client.PostAsync("/welkom", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ReturnUrl"] = returnUrl,
            ["Name"] = " Jan de Boer ",
            ["Office"] = "amsterdam",
        }), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe(expectedLocation);
        var put = _reservations.Requests.Where(r => r.Method == HttpMethod.Put).ShouldHaveSingleItem();
        put.Body.ShouldBe("""{"name":"Jan de Boer","homeOffice":"amsterdam"}""");
        put.Headers[ActingUserHandler.EmployeeHeader].ShouldBe(TestPrincipals.UnknownId.ToString());
    }

    [Fact]
    public async Task PostingUnknownOffice_ShowsMessage()
    {
        using var client = CreateClient(TestPrincipals.UnknownId);
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await client.PostAsync("/welkom", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Name"] = "Jan de Boer",
            ["Office"] = "rotterdam",
        }), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain("Kies je vestiging.");
        _reservations.Requests.ShouldNotContain(r => r.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task PostingWhenApiRejects_ShowsDutchDetail()
    {
        _reservations.On(HttpMethod.Put, $"/employees/{TestPrincipals.UnknownId}", _ =>
            StubHttpHandler.Problem(HttpStatusCode.UnprocessableEntity, "invalid", "Kies je kantoor."));
        using var client = CreateClient(TestPrincipals.UnknownId);
        var token = await GetAntiforgeryTokenAsync(client);

        using var response = await client.PostAsync("/welkom", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Name"] = "Jan de Boer",
            ["Office"] = "utrecht",
        }), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain("Kies je kantoor.");
    }

    [Fact]
    public async Task UnknownEmployee_OnHtmxRequest_GetsFullPageRedirect()
    {
        using var client = CreateClient(TestPrincipals.UnknownId);
        client.DefaultRequestHeaders.Add("HX-Request", "true");

        using var response = await client.GetAsync("/wagenpark", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.GetValues("HX-Redirect").ShouldHaveSingleItem().ShouldBe("/welkom?returnUrl=%2Fwagenpark");
    }

    [Fact]
    public async Task PostingOffice_ShowsToastOnNextPage()
    {
        using var client = CreateClient(TestPrincipals.UnknownId);
        var token = await GetAntiforgeryTokenAsync(client);
        using var post = await client.PostAsync("/welkom", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Name"] = "Jan de Boer",
            ["Office"] = "amsterdam",
        }), Token);
        _reservations.OnJson(HttpMethod.Get, $"/employees/{TestPrincipals.UnknownId}", EmployeeJson(TestPrincipals.UnknownId, "Jan de Boer"));

        var home = await client.GetStringAsync("/", Token);
        var next = await client.GetStringAsync("/", Token);

        home.ShouldContain("Je thuislocatie is Amsterdam.");
        home.ShouldContain("data-toast-close");
        next.ShouldNotContain("Je thuislocatie is Amsterdam.");
    }

    [Theory]
    [InlineData("/welkom", true)]
    [InlineData("/Welkom", true)]
    [InlineData("/health", true)]
    [InlineData("/alive", true)]
    [InlineData("/.auth/login/aad", true)]
    [InlineData("/css/site.css", true)]
    [InlineData("/lib/htmx/htmx-2.0.11.min.js", true)]
    [InlineData("/", false)]
    [InlineData("/wagenpark", false)]
    [InlineData("/welkomst", false)]
    public void IsExcluded_SkipsWelcomeHealthAuthAndStaticFiles(string path, bool expected)
    {
        OnboardingPageFilter.IsExcluded(new PathString(path)).ShouldBe(expected);
    }

    private HttpClient CreateClient(Guid userId)
    {
        var customFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddHttpClient<FleetApiClient>().ConfigurePrimaryHttpMessageHandler(() => _fleet);
            services.AddHttpClient<ReservationsApiClient>().ConfigurePrimaryHttpMessageHandler(() => _reservations);
        }));
        var client = customFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(EasyAuthAuthenticationDefaults.HeaderName, TestPrincipals.Header(userId));
        return client;
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/welkom", Token);
        return TokenPattern().Match(html).Groups[1].Value;
    }

    private static string EmployeeJson(Guid id, string name) =>
        $$"""{"id":"{{id}}","name":"{{name}}","homeOffice":"utrecht","kind":"SignedIn"}""";

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}
