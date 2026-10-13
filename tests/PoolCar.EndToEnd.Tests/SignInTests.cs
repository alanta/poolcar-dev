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
}
