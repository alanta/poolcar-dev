using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using PoolCar.Web.Auth;
using PoolCar.Web.Clients;
using PoolCar.Web.Desk;
using PoolCar.Web.Employees;
using PoolCar.Web.Formatting;
using PoolCar.Web.Htmx;
using PoolCar.Web.Offices;
using PoolCar.Web.Schedule;

namespace PoolCar.Web.Pages;

/// <summary>The desk of an office: cars to hand out, cars out, cars due back and overdue cars.</summary>
[Authorize(Policy = PoolCarPolicies.Operator)]
public class BalieModel(
    CurrentEmployee currentEmployee,
    OfficeDirectory officeDirectory,
    ScheduleService scheduleService,
    ReservationsApiClient reservationsApiClient,
    TimeProvider timeProvider) : PageModel
{
    public const string BoardPartial = "_BalieBoard";
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>How far back overdue periods are looked up.</summary>
    private const int OverdueDays = 14;

    private const int FutureDays = 30;

    /// <summary>How far ahead bookings at risk are looked up.</summary>
    private const int AtRiskDays = 7;

    private ScheduleView? _schedule;

    [BindProperty(SupportsGet = true, Name = "vestiging")]
    public string? OfficeId { get; set; }

    [BindProperty(SupportsGet = true, Name = "datum")]
    public string? Datum { get; set; }

    public IReadOnlyList<FleetOffice> Offices { get; private set; } = [];
    public FleetOffice? SelectedOffice { get; private set; }
    public DateOnly Today { get; private set; }
    public DateOnly Date { get; private set; }
    public DateTimeOffset Now { get; private set; }
    public DeskBoard Board { get; private set; } = DeskBoard.Empty;

    /// <summary>A toast to show next to the board after an action.</summary>
    public Toast? Toast { get; private set; }

    public bool IsToday => Date == Today;

    public string DateValue => Date.ToString(DateFormat, CultureInfo.InvariantCulture);

    public string DateText => DutchDate.Capitalize(DutchDate.Long(Date));

    public string UpdatedAt => AmsterdamTime.Time(Now);

    /// <summary>"3 uit, 2 terug, 1 te laat".</summary>
    public string Counters => $"{Board.OutCount} uit, {Board.ReturnedCount} terug, {Board.OverdueCount} te laat";

    public string BoardUrl => QueryHelpers.AddQueryString("/balie", new Dictionary<string, string?>
    {
        ["handler"] = "Board",
        ["vestiging"] = SelectedOffice?.Id,
        ["datum"] = DateValue,
    });

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync(HttpContext.RequestAborted);
        Response.Headers.Vary = HtmxRequestExtensions.RequestHeader;
        return this.PartialOrPage(BoardPartial, this);
    }

    /// <summary>The board only, polled by htmx.</summary>
    public async Task<IActionResult> OnGetBoardAsync()
    {
        await LoadAsync(HttpContext.RequestAborted);
        return Partial(BoardPartial, this);
    }

    /// <summary>Hands out a car on behalf of the employee.</summary>
    public Task<IActionResult> OnPostPickUpAsync(Guid id) =>
        ActAsync(id, reservationsApiClient.PickUpAsync, (item, name) => $"{item} uitgegeven aan {name}.");

    /// <summary>Takes a car back on behalf of the employee.</summary>
    public Task<IActionResult> OnPostReturnAsync(Guid id) =>
        ActAsync(id, reservationsApiClient.ReturnAsync, (item, name) => $"{item} van {name} ingeleverd.");

    private async Task<IActionResult> ActAsync(
        Guid periodId,
        Func<Guid, CancellationToken, Task<ApiResult<Reservation>>> action,
        Func<string, string, string> message)
    {
        var result = await action(periodId, CancellationToken.None);
        await LoadAsync(HttpContext.RequestAborted);

        Toast = result.IsSuccess
            ? Toast.Success(message(DescribeCar(result.Value.CarId), DescribeEmployee(periodId)))
            : Toast.Error(result.Problem.Detail);

        if (Request.IsHtmx())
        {
            return Partial(BoardPartial, this);
        }

        Toast.Store(TempData);
        return Redirect(QueryHelpers.AddQueryString("/balie", new Dictionary<string, string?>
        {
            ["vestiging"] = SelectedOffice?.Id,
            ["datum"] = DateValue,
        }));
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        await currentEmployee.LoadAsync(cancellationToken);
        Now = timeProvider.GetUtcNow();
        Today = AmsterdamTime.Today(timeProvider);
        Date = DateOnly.TryParseExact(Datum, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            && date >= Today.AddDays(-OverdueDays)
            && date <= Today.AddDays(FutureDays)
                ? date
                : Today;

        Offices = await officeDirectory.GetOfficesAsync(cancellationToken);
        SelectedOffice = Find(OfficeId) ?? Find(currentEmployee.HomeOffice) ?? Offices.FirstOrDefault();
        if (SelectedOffice is null)
        {
            return;
        }

        var to = Date > Today.AddDays(AtRiskDays) ? Date : Today.AddDays(AtRiskDays);
        _schedule = await scheduleService.GetOfficeScheduleAsync(
            SelectedOffice.Id, null, Today.AddDays(-OverdueDays), to, cancellationToken);
        Board = DeskBoard.Build(_schedule, Date, Now);
    }

    private string DescribeCar(string plate)
    {
        var car = _schedule?.Rows.FirstOrDefault(r => string.Equals(r.Car.Plate, plate, StringComparison.OrdinalIgnoreCase))?.Car;
        var formatted = LicensePlate.Format(plate);
        return car is null ? formatted : $"{car.Make} {car.Model} ({formatted})";
    }

    private string DescribeEmployee(Guid periodId) =>
        _schedule?.Rows.SelectMany(r => r.Periods).FirstOrDefault(p => p.PeriodId == periodId)?.EmployeeFirstName
            ?? "de medewerker";

    private FleetOffice? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : Offices.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));
}
