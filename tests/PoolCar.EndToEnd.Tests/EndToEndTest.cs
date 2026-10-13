using Microsoft.Playwright;

namespace PoolCar.EndToEnd.Tests;

/// <summary>Gives each test its own browser context and page, and keeps a trace of failed tests.</summary>
public abstract class EndToEndTest(PoolCarApp app) : IAsyncLifetime
{
    private const string EmployeeRole = "Employee";
    private const string FleetOperatorRole = "FleetOperator";

    private IBrowserContext? _context;

    protected IPage Page { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _context = await app.Browser.NewContextAsync(new() { BaseURL = app.ProxyUrl.ToString() });
        await _context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
        Page = await _context.NewPageAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_context is null)
        {
            return;
        }

        if (TestContext.Current.TestState?.Result == TestResult.Failed)
        {
            var path = TracePath();
            await _context.Tracing.StopAsync(new() { Path = path });
            TestContext.Current.TestOutputHelper?.WriteLine($"Playwright trace: {path}");
        }
        else
        {
            await _context.Tracing.StopAsync();
        }

        await _context.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Signs in through the EasyAuth proxy as an employee and waits for the home page.</summary>
    protected Task SignInAsync(Employee employee) => SignInAsync(employee, EmployeeRole);

    /// <summary>Signs in through the EasyAuth proxy as a fleet operator and waits for the home page.</summary>
    protected Task SignInAsOperatorAsync(Employee employee) => SignInAsync(employee, FleetOperatorRole);

    private async Task SignInAsync(Employee employee, string role)
    {
        await Page.GotoAsync("/");
        await Page.GetByLabel("User ID").FillAsync(employee.Id.ToString());
        await Page.GetByLabel("Username").FillAsync(employee.Name);
        await Page.GetByLabel("Roles").FillAsync(role);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();

        await Assertions.Expect(Page.GetByRole(AriaRole.Banner)).ToContainTextAsync(employee.Name);
    }

    private static string TracePath()
    {
        var testName = TestContext.Current.Test?.TestDisplayName ?? Guid.NewGuid().ToString();
        var fileName = string.Concat(testName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "playwright-traces");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{fileName}.zip");
    }
}
