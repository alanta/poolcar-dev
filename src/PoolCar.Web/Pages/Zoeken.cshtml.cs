using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PoolCar.Web.Clients;
using PoolCar.Web.Offices;

namespace PoolCar.Web.Pages;

public class ZoekenModel(FleetApiClient fleetApiClient, OfficeDirectory officeDirectory) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "kenteken")]
    public string? Query { get; set; }

    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

    public IReadOnlyList<SearchResult> Results { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!HasQuery)
        {
            return Page();
        }

        var cancellationToken = HttpContext.RequestAborted;
        var cars = await fleetApiClient.SearchCarsAsync(Query!, cancellationToken);
        if (cars.Count == 1)
        {
            return Redirect($"/auto/{Uri.EscapeDataString(cars[0].Plate)}");
        }

        var results = new List<SearchResult>();
        foreach (var car in cars)
        {
            results.Add(new SearchResult(car, await officeDirectory.GetNameAsync(car.Office, cancellationToken)));
        }

        Results = results;
        return Page();
    }

    public sealed record SearchResult(FleetCar Car, string? OfficeName);
}
