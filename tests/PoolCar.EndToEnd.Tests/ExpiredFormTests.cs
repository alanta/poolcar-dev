using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace PoolCar.EndToEnd.Tests;

/// <summary>Removing the antiforgery cookie makes the tokens of an open page invalid, like a restart of the app with
/// new data protection keys does.</summary>
[Collection(PoolCarAppCollection.Name)]
public partial class ExpiredFormTests(PoolCarApp app) : EndToEndTest(app)
{
    private const string ExpiredMessage = "Deze pagina is verlopen. Laad de pagina opnieuw en probeer het nog eens.";

    [Fact]
    public async Task Expired_form_shows_a_page_to_reload_the_form()
    {
        var employee = SeededEmployees.Utrecht;
        await SignInAsync(employee);
        await Page.GotoAsync("/thuislocatie");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Thuislocatie");

        await ExpireAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = employee.Office, Exact = true }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Pagina verlopen");
        await Expect(Page.GetByText(ExpiredMessage)).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Pagina opnieuw laden" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Thuislocatie");
        await Page.GetByRole(AriaRole.Button, new() { Name = employee.Office, Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = $"Je thuislocatie is {employee.Office}." }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task Expired_htmx_form_shows_a_toast_to_reload_the_page()
    {
        await SignInAsync(SeededEmployees.Amsterdam);
        await NavigateToAsync("Reserveren", "Auto reserveren");
        var date = DutchCalendar.Today.AddDays(5);
        await Page.GetByLabel("Van", new() { Exact = true }).FillAsync(DutchCalendar.Iso(date));
        await Page.GetByLabel("Tot en met").FillAsync(DutchCalendar.Iso(date));
        await Page.GetByRole(AriaRole.Radio, new() { Name = "Ochtend" }).CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Toon vrije auto's" }).ClickAsync();
        await Page.GetByRole(AriaRole.Region, new() { Name = "Kies een categorie" })
            .GetByRole(AriaRole.Link, new() { Name = "Middel" })
            .ClickAsync();
        await Page.GetByRole(AriaRole.Region, new() { Name = "Kies een auto" })
            .GetByRole(AriaRole.Link, new() { Name = "Kies" })
            .First
            .ClickAsync();
        var confirmation = Page.GetByRole(AriaRole.Region, new() { Name = "Bevestig je reservering" });
        await Expect(confirmation).ToBeVisibleAsync();

        await ExpireAsync();
        await confirmation.GetByRole(AriaRole.Button, new() { Name = "Reserveer", Exact = true }).ClickAsync();

        var toast = Page.GetByRole(AriaRole.Alert).Filter(new() { HasText = ExpiredMessage });
        await Expect(toast).ToBeVisibleAsync();
        await Expect(confirmation).ToBeVisibleAsync();
        await toast.GetByRole(AriaRole.Link, new() { Name = "Pagina opnieuw laden" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Auto reserveren");
        await Expect(confirmation).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Alert).Filter(new() { HasText = ExpiredMessage })).ToHaveCountAsync(0);
    }

    private Task ExpireAsync() =>
        Page.Context.ClearCookiesAsync(new() { NameRegex = AntiforgeryCookie() });

    [GeneratedRegex(@"^\.AspNetCore\.Antiforgery\.")]
    private static partial Regex AntiforgeryCookie();
}
