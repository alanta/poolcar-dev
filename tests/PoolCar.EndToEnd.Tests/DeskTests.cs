using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace PoolCar.EndToEnd.Tests;

[Collection(PoolCarAppCollection.Name)]
public class DeskTests(PoolCarApp app) : EndToEndTest(app)
{
    // Seeded in Utrecht: the van V-62-PLT is picked up for today, the Mini GNK-21-N was not returned yesterday
    private const string PickedUpTodayPlate = "V-62-PLT";
    private const string NotReturnedPlate = "GNK-21-N";

    [Fact]
    public async Task Operator_sees_todays_pickups_and_returns()
    {
        Assert.SkipWhen(
            DutchCalendar.Now.Hour is < 8 or >= 17,
            "The desk is tested during office hours (08:00–17:00).");
        var today = DutchCalendar.Today;
        await SignInAsOperatorAsync(SeededEmployees.UtrechtOperator);

        await NavigateToAsync("Balie");
        await Page.GetByLabel("Vestiging").SelectOptionAsync(new SelectOptionValue { Label = "Utrecht" });
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 2, Name = $"{DutchCalendar.Long(today)} · Utrecht" }))
            .ToBeVisibleAsync();

        var outOrDueBack = Column("Onderweg").Or(Column("Inleveren verwacht"));
        var pickedUpToday = outOrDueBack.GetByRole(AriaRole.Listitem).Filter(new() { HasText = PickedUpTodayPlate });
        await Expect(pickedUpToday).ToHaveCountAsync(1);
        await Expect(pickedUpToday).ToContainTextAsync("Hele dag");

        var notReturned = Column("Te laat").GetByRole(AriaRole.Listitem).Filter(new() { HasText = NotReturnedPlate });
        await Expect(notReturned).ToContainTextAsync(DutchCalendar.ShortLower(today.AddDays(-1)));
        await Expect(notReturned).ToContainTextAsync("Hele dag");
    }

    private ILocator Column(string title) =>
        Page.GetByRole(AriaRole.Region, new() { NameRegex = new Regex($"^{Regex.Escape(title)}\\b") });
}
