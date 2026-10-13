using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PoolCar.Web.Clients;
using PoolCar.Web.Employees;
using PoolCar.Web.Htmx;
using PoolCar.Web.Offices;

namespace PoolCar.Web.Pages;

public class WagenparkModel(
    FleetApiClient fleetApiClient,
    OfficeDirectory officeDirectory,
    CurrentEmployee currentEmployee) : PageModel
{
    private const string DefaultOfficeId = "utrecht";

    [BindProperty(SupportsGet = true, Name = "vestiging")]
    public string? OfficeId { get; set; }

    [BindProperty(SupportsGet = true, Name = "categorie")]
    public string? CategoryId { get; set; }

    public IReadOnlyList<FleetOffice> Offices { get; private set; } = [];
    public IReadOnlyList<FleetCategory> Categories { get; private set; } = [];
    public FleetOffice? SelectedOffice { get; private set; }
    public FleetCategory? SelectedCategory { get; private set; }
    public IReadOnlyList<CategoryGroup> Groups { get; private set; } = [];
    public int CarCount { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        await currentEmployee.LoadAsync(cancellationToken);
        Offices = await officeDirectory.GetOfficesAsync(cancellationToken);
        SelectedOffice = FindOffice(OfficeId)
            ?? FindOffice(currentEmployee.HomeOffice)
            ?? FindOffice(DefaultOfficeId)
            ?? Offices.FirstOrDefault();

        if (SelectedOffice is not null)
        {
            Categories = [.. (await fleetApiClient.GetCategoriesAsync(cancellationToken)).OrderBy(c => c.SortOrder)];
            SelectedCategory = Categories.FirstOrDefault(c => string.Equals(c.Id, CategoryId, StringComparison.OrdinalIgnoreCase));

            var cars = await fleetApiClient.GetCarsAsync(SelectedOffice.Id, SelectedCategory?.Id, cancellationToken);
            CarCount = cars.Count;
            Groups = CategoryGroup.Create(Categories, cars);
        }

        return this.PartialOrPage("_WagenparkCars", this);
    }

    private FleetOffice? FindOffice(string? id) =>
        string.IsNullOrEmpty(id)
            ? null
            : Offices.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>The cars of a category, with a summary line such as "Middel, 14 auto's, gemiddeld 480 km
/// bereik".</summary>
public sealed record CategoryGroup(FleetCategory Category, IReadOnlyList<FleetCar> Cars)
{
    public int AverageRangeKm => Cars.Count == 0
        ? 0
        : (int)Math.Round(Cars.Average(c => c.RangeKm), MidpointRounding.AwayFromZero);

    public string Summary =>
        $"{Category.Name}, {Cars.Count} {(Cars.Count == 1 ? "auto" : "auto's")}, gemiddeld {AverageRangeKm} km bereik";

    /// <summary>A group per category with cars, in category order.</summary>
    public static IReadOnlyList<CategoryGroup> Create(IEnumerable<FleetCategory> categories, IReadOnlyList<FleetCar> cars) =>
        [.. categories
            .OrderBy(c => c.SortOrder)
            .Select(c => new CategoryGroup(c, [.. cars.Where(car => car.Category == c.Id)]))
            .Where(g => g.Cars.Count > 0)];
}
