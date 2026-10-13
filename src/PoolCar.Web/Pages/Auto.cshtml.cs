using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PoolCar.Web.Cars;
using PoolCar.Web.Clients;
using PoolCar.Web.Employees;
using PoolCar.Web.Formatting;
using PoolCar.Web.Offices;
using PoolCar.Web.Schedule;

namespace PoolCar.Web.Pages;

public class AutoModel(
    ScheduleService scheduleService,
    FleetApiClient fleetApiClient,
    ReservationsApiClient reservationsApiClient,
    OfficeDirectory officeDirectory,
    CurrentEmployee currentEmployee,
    TimeProvider timeProvider) : PageModel
{
    /// <summary>The schedule is fetched for this many days from Monday, the most the schedule allows.</summary>
    private const int ScheduleDays = 62;

    [BindProperty(SupportsGet = true)]
    public string Kenteken { get; set; } = string.Empty;

    public FleetCar? Car { get; private set; }
    public FleetCategory? Category { get; private set; }
    public string? OfficeName { get; private set; }
    public DateOnly Monday { get; private set; }
    public IReadOnlyList<WeekDay> Week { get; private set; } = [];
    public IReadOnlyList<CarBooking> Upcoming { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        var today = AmsterdamTime.Today(timeProvider);
        Monday = CarWeek.StartOfWeek(today);

        var row = await scheduleService.GetCarScheduleAsync(Kenteken, Monday, Monday.AddDays(ScheduleDays - 1), cancellationToken);
        if (row is null)
        {
            return new PageResult { StatusCode = StatusCodes.Status404NotFound };
        }

        Car = row.Car;
        var categories = await fleetApiClient.GetCategoriesAsync(cancellationToken);
        Category = categories.FirstOrDefault(c => c.Id == Car.Category);
        OfficeName = await officeDirectory.GetNameAsync(Car.Office, cancellationToken);

        var mine = await GetMyReservationIdsAsync(cancellationToken);
        Week = CarWeek.Create(row, today, mine);
        Upcoming = CarWeek.Upcoming(row, timeProvider.GetUtcNow(), mine);
        return Page();
    }

    private async Task<IReadOnlySet<Guid>> GetMyReservationIdsAsync(CancellationToken cancellationToken)
    {
        await currentEmployee.LoadAsync(cancellationToken);
        if (!currentEmployee.IsKnown)
        {
            return new HashSet<Guid>();
        }

        var reservations = await reservationsApiClient.GetMyReservationsAsync(cancellationToken);
        return reservations.Select(r => r.Id).ToHashSet();
    }
}
