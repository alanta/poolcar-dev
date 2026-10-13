using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PoolCar.Web.Clients;

namespace PoolCar.Web.Pages;

public class WagenparkModel(FleetApiClient fleetApiClient) : PageModel
{
    private const string DefaultOfficeId = "utrecht";

    [BindProperty(SupportsGet = true, Name = "vestiging")]
    public string? OfficeId { get; set; }

    public IReadOnlyList<FleetOffice> Offices { get; private set; } = [];
    public FleetOffice? SelectedOffice { get; private set; }
    public IReadOnlyList<CategoryGroup> Groups { get; private set; } = [];
    public int CarCount { get; private set; }

    public async Task OnGetAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        Offices = await fleetApiClient.GetOfficesAsync(cancellationToken);
        SelectedOffice = Offices.FirstOrDefault(o => string.Equals(o.Id, OfficeId, StringComparison.OrdinalIgnoreCase))
            ?? Offices.FirstOrDefault(o => o.Id == DefaultOfficeId)
            ?? Offices.FirstOrDefault();

        if (SelectedOffice is null)
        {
            return;
        }

        var categories = await fleetApiClient.GetCategoriesAsync(cancellationToken);
        var cars = await fleetApiClient.GetCarsAsync(office: SelectedOffice.Id, cancellationToken: cancellationToken);

        CarCount = cars.Count;
        Groups = categories
            .OrderBy(c => c.SortOrder)
            .Select(c => new CategoryGroup(c, cars.Where(car => car.Category == c.Id).ToList()))
            .Where(g => g.Cars.Count > 0)
            .ToList();
    }

    public sealed record CategoryGroup(FleetCategory Category, IReadOnlyList<FleetCar> Cars);
}
