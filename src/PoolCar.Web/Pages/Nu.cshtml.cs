using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PoolCar.Web.Availability;
using PoolCar.Web.Clients;
using PoolCar.Web.Employees;
using PoolCar.Web.Formatting;
using PoolCar.Web.Htmx;
using PoolCar.Web.Offices;

namespace PoolCar.Web.Pages;

public class NuModel(
    CurrentEmployee currentEmployee,
    OfficeDirectory officeDirectory,
    AvailabilityService availabilityService,
    ReservationsApiClient reservationsApiClient,
    FleetApiClient fleetApiClient,
    TimeProvider timeProvider) : PageModel
{
    public const string CarsPartial = "_NowCars";

    [BindProperty(SupportsGet = true, Name = "vestiging")]
    public string? OfficeId { get; set; }

    public IReadOnlyList<FleetOffice> Offices { get; private set; } = [];

    public FleetOffice? SelectedOffice { get; private set; }

    /// <summary>The current morning or afternoon, or null outside day-part hours.</summary>
    public DayPart? DayPart { get; private set; }

    /// <summary>"Vrij tot 12:00".</summary>
    public string? FreeUntil => DayPart is { } part ? $"Vrij tot {DayPartText.End(part):HH\\:mm}" : null;

    public IReadOnlyList<CategoryCars> Groups { get; private set; } = [];

    public string? ErrorMessage { get; private set; }

    /// <summary>Reserveren for tomorrow morning at the selected office.</summary>
    public string TomorrowUrl
    {
        get
        {
            var tomorrow = AmsterdamTime.Today(timeProvider).AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var office = Uri.EscapeDataString(SelectedOffice?.Id ?? string.Empty);
            return $"/reserveren?van={tomorrow}&dagdeel={DayPartText.Slug(Clients.DayPart.Morning)}&vestiging={office}";
        }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        return this.PartialOrPage(CarsPartial, this);
    }

    public async Task<IActionResult> OnPostAsync(string? carId)
    {
        var cancellationToken = HttpContext.RequestAborted;
        var result = await reservationsApiClient.BookNowAsync(carId ?? string.Empty, cancellationToken);
        if (result.IsSuccess)
        {
            var car = await fleetApiClient.GetCarAsync(result.Value.CarId, cancellationToken);
            var plate = LicensePlate.Format(result.Value.CarId);
            var name = car is null ? "auto" : $"{car.Make} {car.Model}";
            Toast.Success($"Goede reis! De {name} {plate} staat voor je klaar.").Store(TempData);
            return this.HtmxRedirect(MijnReserveringenModel.Path);
        }

        ErrorMessage = result.Problem.Detail;
        await LoadAsync();
        if (!Request.IsHtmxPartial())
        {
            return Page();
        }

        var status = result.Problem.IsConflict || result.Problem.IsInvalid
            ? result.Problem.Status
            : StatusCodes.Status200OK;
        return this.Partial(CarsPartial, this, status);
    }

    private async Task LoadAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        await currentEmployee.LoadAsync(cancellationToken);
        Offices = await officeDirectory.GetOfficesAsync(cancellationToken);
        SelectedOffice = Find(OfficeId) ?? Find(currentEmployee.HomeOffice) ?? Offices.FirstOrDefault();
        OfficeId = SelectedOffice?.Id;

        DayPart = CurrentDayPart.Now(timeProvider);
        if (SelectedOffice is null || DayPart is null)
        {
            return;
        }

        var availability = await availabilityService.GetAvailableNowAsync(SelectedOffice.Id, cancellationToken: cancellationToken);
        Groups = availability.Categories
            .Select(c => new CategoryCars(c.Category, [.. availability.Cars.Where(car => car.Category == c.Category.Id)]))
            .Where(g => g.Cars.Count > 0)
            .ToList();
    }

    private FleetOffice? Find(string? officeId) =>
        Offices.FirstOrDefault(o => string.Equals(o.Id, officeId, StringComparison.OrdinalIgnoreCase));

    public sealed record CategoryCars(FleetCategory Category, IReadOnlyList<FleetCar> Cars);
}
