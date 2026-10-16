using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace PoolCar.EndToEnd.Tests;

[Collection(PoolCarAppCollection.Name)]
public class SignInTests(PoolCarApp app) : EndToEndTest(app)
{
    [Fact]
    public async Task Employee_signs_in_and_sees_the_home_page()
    {
        var employee = SeededEmployees.Utrecht;

        await SignInAsync(employee);

        await Expect(Page).ToHaveTitleAsync(new Regex("PoolCar"));
        await Expect(Page.GetByRole(AriaRole.Banner)).ToContainTextAsync(employee.Name);
        new Uri(Page.Url).AbsolutePath.ShouldBe("/");
    }

    [Fact]
    public async Task Employee_signs_out_and_sees_the_sign_in_form()
    {
        await SignInAsync(SeededEmployees.Utrecht);

        await Page.Locator("summary", new() { HasText = "Gebruikersmenu van" }).ClickAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Afmelden" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Login" })).ToBeVisibleAsync();
        (await Page.Context.CookiesAsync()).ShouldNotContain(c => c.Name == "EasyAuthDev");
    }
}
