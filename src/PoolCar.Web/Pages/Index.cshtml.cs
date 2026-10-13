using Microsoft.AspNetCore.Mvc.RazorPages;
using PoolCar.Web.Clients;
using PoolCar.Web.Employees;
using PoolCar.Web.Formatting;
using PoolCar.Web.Home;
using PoolCar.Web.Offices;

namespace PoolCar.Web.Pages;

public class IndexModel(
    CurrentEmployee currentEmployee,
    ReservationsApiClient reservationsApiClient,
    FleetApiClient fleetApiClient,
    OfficeDirectory officeDirectory,
    TimeProvider timeProvider) : PageModel
{
    public string FirstName { get; private set; } = string.Empty;

    public bool IsOperator { get; private set; }

    public NextReservationView? Next { get; private set; }

    public async Task OnGetAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        await currentEmployee.LoadAsync(cancellationToken);
        FirstName = currentEmployee.FirstName;
        IsOperator = currentEmployee.IsOperator;

        var reservations = await reservationsApiClient.GetMyReservationsAsync(cancellationToken);
        var next = NextReservation.Find(reservations, timeProvider.GetUtcNow());
        if (next is not null)
        {
            Next = await CreateViewAsync(next, cancellationToken);
        }
    }

    private async Task<NextReservationView> CreateViewAsync(UpcomingPeriod next, CancellationToken cancellationToken)
    {
        var (reservation, period) = next;
        var car = await fleetApiClient.GetCarAsync(reservation.CarId, cancellationToken);
        var officeName = await officeDirectory.GetNameAsync(car?.Office, cancellationToken);

        return new NextReservationView(
            car is null ? null : $"{car.Make} {car.Model}",
            LicensePlate.Format(reservation.CarId),
            DutchDate.Capitalize(DutchDate.Long(period.Date)),
            $"{DayPartText.Label(reservation.DayPart)}, {DayPartText.Hours(reservation.DayPart)}",
            officeName,
            period.Status == PeriodStatus.PickedUp,
            period.Overdue);
    }

    public sealed record NextReservationView(
        string? CarName,
        string Plate,
        string Date,
        string DayPart,
        string? OfficeName,
        bool PickedUp,
        bool Overdue);
}
