using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace PoolCar.EndToEnd.Tests;

[Collection(PoolCarAppCollection.Name)]
public class ReservationTests(PoolCarApp app) : EndToEndTest(app)
{
    // Seeded: the Skoda Elroq KSP-73-B in Utrecht is reserved tomorrow morning
    private const string TakenCarPlate = "KSP73B";

    [Fact]
    public async Task Later_start_date_updates_end_date_without_an_error_or_losing_focus()
    {
        var date = DutchCalendar.Iso(DutchCalendar.Today.AddDays(3));
        await SignInAsync(SeededEmployees.Amsterdam);
        await NavigateToAsync("Reserveren", "Auto reserveren");
        var from = Page.GetByLabel("Van", new() { Exact = true });
        var to = Page.GetByLabel("Tot en met");

        await from.FillAsync(date);

        await Expect(to).ToHaveValueAsync(date);
        await Expect(to).ToHaveAttributeAsync("min", date);
        await Expect(from).ToBeFocusedAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Region, new() { Name = "Kies een categorie" })
            .GetByRole(AriaRole.Link, new() { Name = "Middel" })
            .ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Kies een auto" })).ToBeVisibleAsync();
        await Expect(to).ToHaveValueAsync(date);
        await Expect(to).ToHaveAttributeAsync("min", date);
    }

    [Fact]
    public async Task Dates_can_be_moved_earlier_without_JavaScript()
    {
        await SignInAsync(SeededEmployees.Amsterdam);
        await using var context = await Page.Context.Browser!.NewContextAsync(new()
        {
            BaseURL = new Uri(Page.Url).GetLeftPart(UriPartial.Authority),
            JavaScriptEnabled = false,
            StorageState = await Page.Context.StorageStateAsync(),
        });
        var page = await context.NewPageAsync();
        var originalDate = DutchCalendar.Iso(DutchCalendar.Today.AddDays(5));
        var from = DutchCalendar.Iso(DutchCalendar.Today.AddDays(3));
        var to = DutchCalendar.Iso(DutchCalendar.Today.AddDays(4));
        await page.GotoAsync($"/reserveren?van={originalDate}&tot={originalDate}");

        await page.GetByLabel("Van", new() { Exact = true }).FillAsync(from);
        await page.GetByLabel("Tot en met").FillAsync(to);
        await page.GetByRole(AriaRole.Button, new() { Name = "Toon vrije auto's" }).ClickAsync();

        await Expect(page.GetByLabel("Tot en met")).ToHaveAttributeAsync("min", from);
        await Expect(page.GetByLabel("Tot en met")).ToHaveValueAsync(to);
        await Expect(page.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Kies een categorie" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task New_date_choice_replaces_an_in_flight_request()
    {
        await SignInAsync(SeededEmployees.Amsterdam);
        await NavigateToAsync("Reserveren", "Auto reserveren");
        var delayedRequest = new TaskCompletionSource<IRoute>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Page.RouteAsync("**/reserveren?**", route =>
            delayedRequest.TrySetResult(route) ? Task.CompletedTask : route.ContinueAsync());

        await Page.GetByLabel("Vestiging").SelectOptionAsync("utrecht");
        var delayedRoute = await delayedRequest.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var date = DutchCalendar.Iso(DutchCalendar.Today.AddDays(3));
        var to = Page.GetByLabel("Tot en met");
        try
        {
            await Page.RunAndWaitForResponseAsync(
                () => to.FillAsync(date),
                response => response.Url.Contains($"tot={date}", StringComparison.Ordinal));
            await Expect(Page.Locator(".reserve-summary")).ToContainTextAsync(DutchCalendar.ShortLower(DutchCalendar.Today.AddDays(3)));
            await Expect(to).ToHaveValueAsync(date);
            await Expect(to).ToBeFocusedAsync();
        }
        finally
        {
            await delayedRoute.AbortAsync();
        }
    }

    [Fact]
    public async Task Employee_reserves_a_car_and_sees_it_in_the_planning()
    {
        var employee = SeededEmployees.Amsterdam;
        var date = DutchCalendar.Today.AddDays(3);
        await SignInAsync(employee);

        await NavigateToAsync("Reserveren", "Auto reserveren");
        await ChooseDatesAsync(date, "Middag");
        await Expect(Page.GetByText($"{DutchCalendar.Short(date)}, middag, {employee.Office}")).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Region, new() { Name = "Kies een categorie" })
            .GetByRole(AriaRole.Link, new() { Name = "Middel" })
            .ClickAsync();
        var firstFreeCar = Page.GetByRole(AriaRole.Region, new() { Name = "Kies een auto" })
            .GetByRole(AriaRole.Link, new() { Name = "Kies" })
            .First;
        var plate = PlateIn(await firstFreeCar.TextContentAsync());
        await firstFreeCar.ClickAsync();

        var confirmation = Page.GetByRole(AriaRole.Region, new() { Name = "Bevestig je reservering" });
        await Expect(confirmation).ToContainTextAsync(plate);
        await Expect(confirmation).ToContainTextAsync(DutchCalendar.Short(date));
        await Expect(confirmation).ToContainTextAsync("Middag");
        await confirmation.GetByRole(AriaRole.Button, new() { Name = "Reserveer", Exact = true }).ClickAsync();

        var reserved = Page.GetByRole(AriaRole.Region, new() { Name = "Gereserveerd!" });
        await Expect(reserved).ToContainTextAsync(plate);
        await Expect(reserved).ToContainTextAsync($"{DutchCalendar.Short(date)}, middag, {employee.Office}");

        await NavigateToAsync("Planning");
        await Page.GetByLabel("Vestiging").SelectOptionAsync(new SelectOptionValue { Label = employee.Office });
        await Page.GetByLabel("Categorie").SelectOptionAsync(new SelectOptionValue { Label = "Middel" });
        if (DutchCalendar.MondayOf(date) > DutchCalendar.MondayOf(DutchCalendar.Today))
        {
            await Expect(PlanningTable(employee.Office, DutchCalendar.Today)).ToBeVisibleAsync();
            await Page.GetByRole(AriaRole.Navigation, new() { Name = "Week kiezen" })
                .GetByRole(AriaRole.Link, new() { Name = "Volgende" })
                .ClickAsync();
        }

        var carRow = PlanningTable(employee.Office, date).GetByRole(AriaRole.Row).Filter(new() { HasText = plate });
        var reservedSlot = carRow.GetByRole(AriaRole.Cell, new() { Name = "Gereserveerd door jou" })
            .And(carRow.GetByTitle(new Regex($@"\(jij\), {Regex.Escape(DutchCalendar.ShortLower(date))}, middag")));
        await Expect(reservedSlot).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Reserving_a_taken_car_shows_a_clear_message()
    {
        var tomorrow = DutchCalendar.Today.AddDays(1);
        await SignInAsync(SeededEmployees.Utrecht);

        await Page.GotoAsync($"/auto/{TakenCarPlate}");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Reserveer deze auto" }).First.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Auto reserveren");
        await ChooseDatesAsync(tomorrow, "Ochtend");

        var conflict = Page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "Deze auto is niet vrij" });
        await Expect(conflict).ToContainTextAsync($"Deze auto is al gereserveerd voor {DutchCalendar.DayAndMonth(tomorrow)}.");
        await conflict.GetByRole(AriaRole.Link, new() { Name = "Kies een andere auto" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Kies een categorie" })).ToBeVisibleAsync();
        await Expect(Page.GetByText($"{DutchCalendar.Short(tomorrow)}, ochtend, Utrecht")).ToBeVisibleAsync();
    }

    private async Task ChooseDatesAsync(DateOnly date, string dayPart)
    {
        await Page.GetByLabel("Van", new() { Exact = true }).FillAsync(DutchCalendar.Iso(date));
        await Page.GetByLabel("Tot en met").FillAsync(DutchCalendar.Iso(date));
        await Page.GetByRole(AriaRole.Radio, new() { Name = dayPart }).CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Toon vrije auto's" }).ClickAsync();
    }

    private ILocator PlanningTable(string office, DateOnly date) =>
        Page.GetByRole(AriaRole.Table, new() { Name = $"Planning {office}, middel, Week {DutchCalendar.Week(date)} ·" });
}
