using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace PoolCar.EndToEnd.Tests;

[Collection(PoolCarAppCollection.Name)]
public class BookNowTests(PoolCarApp app) : EndToEndTest(app)
{
    [Fact]
    public async Task Employee_picks_up_and_returns_a_car()
    {
        Assert.SkipUnless(
            DutchCalendar.Now.Hour is >= 10 and < 17,
            "Booking a car now is tested during office hours (10:00–17:00).");
        var today = DutchCalendar.Today;
        await SignInAsync(SeededEmployees.Eindhoven);

        await NavigateToAsync("Nu een auto");
        var takeFirstFreeCar = Page.GetByRole(AriaRole.Button, new() { Name = "Neem deze auto" }).First;
        var plate = PlateIn(await takeFirstFreeCar.TextContentAsync());
        await takeFirstFreeCar.ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Mijn reserveringen");
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Goede reis!" })).ToContainTextAsync(plate);
        var onTheRoad = Page.GetByRole(AriaRole.Region, new() { Name = "Onderweg", Exact = true });
        var card = onTheRoad.GetByRole(AriaRole.Article).Filter(new() { HasText = plate });
        await Expect(card).ToContainTextAsync(DutchCalendar.Short(today));
        await Expect(card).ToContainTextAsync("Onderweg");

        await card.GetByRole(AriaRole.Button, new() { Name = "Inleveren" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Bedankt!" })).ToContainTextAsync(plate);

        await Page.ReloadAsync();
        await Expect(onTheRoad.GetByRole(AriaRole.Article).Filter(new() { HasText = plate })).ToHaveCountAsync(0);
        var returned = Page.GetByRole(AriaRole.Region, new() { Name = "Afgelopen", Exact = true })
            .GetByRole(AriaRole.Article)
            .Filter(new() { HasText = plate })
            .First;
        await Expect(returned).ToContainTextAsync(DutchCalendar.Short(today));
        await Expect(returned).ToContainTextAsync("Ingeleverd");
    }
}
