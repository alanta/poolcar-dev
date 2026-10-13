using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using PoolCar.Web.Clients;
using PoolCar.Web.Employees;
using PoolCar.Web.Formatting;
using PoolCar.Web.Htmx;
using PoolCar.Web.Offices;
using PoolCar.Web.Planning;
using PoolCar.Web.Schedule;

namespace PoolCar.Web.Pages;

/// <summary>A read-only week grid of the cars of an office, per category.</summary>
public class PlanningModel(
    CurrentEmployee currentEmployee,
    OfficeDirectory officeDirectory,
    FleetApiClient fleetApiClient,
    ScheduleService scheduleService,
    ReservationsApiClient reservationsApiClient,
    TimeProvider timeProvider) : PageModel
{
    public const string BoardPartial = "_PlanningBoard";
    public const string AllCategories = "alle";
    private const string DateFormat = "yyyy-MM-dd";

    [BindProperty(SupportsGet = true, Name = "vestiging")]
    public string? OfficeId { get; set; }

    [BindProperty(SupportsGet = true, Name = "categorie")]
    public string? CategoryId { get; set; }

    [BindProperty(SupportsGet = true, Name = "week")]
    public string? Week { get; set; }

    public IReadOnlyList<FleetOffice> Offices { get; private set; } = [];
    public IReadOnlyList<FleetCategory> Categories { get; private set; } = [];
    public FleetOffice? SelectedOffice { get; private set; }

    /// <summary>The selected category id, or <see cref="AllCategories"/>.</summary>
    public string SelectedCategory { get; private set; } = AllCategories;

    public DateOnly Today { get; private set; }
    public DateOnly Monday { get; private set; }
    public IReadOnlyList<DateOnly> Dates { get; private set; } = [];
    public IReadOnlyList<PlanningRow> Rows { get; private set; } = [];

    public DateOnly Sunday => Monday.AddDays(6);

    public string WeekText =>
        $"Week {ISOWeek.GetWeekOfYear(Monday.ToDateTime(TimeOnly.MinValue))} · {DutchDate.Range(Monday, Sunday)}";

    public string SelectedCategoryName =>
        Categories.FirstOrDefault(c => c.Id == SelectedCategory)?.Name.ToLowerInvariant() ?? "alle categorieën";

    public async Task<IActionResult> OnGetAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        await currentEmployee.LoadAsync(cancellationToken);
        Today = AmsterdamTime.Today(timeProvider);
        Monday = PlanningGrid.MondayOf(
            DateOnly.TryParseExact(Week, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var week)
                ? week
                : Today);

        Offices = await officeDirectory.GetOfficesAsync(cancellationToken);
        SelectedOffice = Find(Offices, OfficeId) ?? Find(Offices, currentEmployee.HomeOffice) ?? Offices.FirstOrDefault();

        Categories = [.. (await fleetApiClient.GetCategoriesAsync(cancellationToken)).OrderBy(c => c.SortOrder)];
        SelectedCategory =
            string.Equals(CategoryId, AllCategories, StringComparison.OrdinalIgnoreCase) ? AllCategories
            : Categories.FirstOrDefault(c => string.Equals(c.Id, CategoryId, StringComparison.OrdinalIgnoreCase))?.Id
                ?? Categories.FirstOrDefault()?.Id
                ?? AllCategories;

        if (SelectedOffice is not null)
        {
            var category = SelectedCategory == AllCategories ? null : SelectedCategory;
            var scheduleTask = scheduleService.GetOfficeScheduleAsync(SelectedOffice.Id, category, Monday, Sunday, cancellationToken);
            var mineTask = GetMyReservationIdsAsync(cancellationToken);
            var schedule = await scheduleTask;
            Dates = schedule.Dates;
            Rows = PlanningGrid.Build(schedule, await mineTask, timeProvider.GetUtcNow());
        }

        // The same URL returns the board for htmx and the full page otherwise
        Response.Headers.Vary = HtmxRequestExtensions.RequestHeader;
        return this.PartialOrPage(BoardPartial, this);
    }

    /// <summary>The planning URL for the current selection, with another week.</summary>
    public string WeekUrl(DateOnly monday) => QueryHelpers.AddQueryString("/planning", new Dictionary<string, string?>
    {
        ["vestiging"] = SelectedOffice?.Id,
        ["categorie"] = SelectedCategory,
        ["week"] = monday.ToString(DateFormat, CultureInfo.InvariantCulture),
    });

    /// <summary>The reservation page for a free half day of a car.</summary>
    public string ReserveUrl(FleetCar car, PlanningSlot slot)
    {
        var date = slot.Date.ToString(DateFormat, CultureInfo.InvariantCulture);
        return QueryHelpers.AddQueryString("/reserveren", new Dictionary<string, string?>
        {
            ["auto"] = car.Plate,
            ["vestiging"] = SelectedOffice?.Id ?? car.Office,
            ["van"] = date,
            ["tot"] = date,
            ["dagdeel"] = DayPartText.Slug(slot.Half),
        });
    }

    public string WeekValue => Monday.ToString(DateFormat, CultureInfo.InvariantCulture);

    private async Task<IReadOnlySet<Guid>> GetMyReservationIdsAsync(CancellationToken cancellationToken)
    {
        if (!currentEmployee.IsKnown)
        {
            return new HashSet<Guid>();
        }

        var reservations = await reservationsApiClient.GetMyReservationsAsync(cancellationToken);
        return reservations.Select(r => r.Id).ToHashSet();
    }

    private static FleetOffice? Find(IReadOnlyList<FleetOffice> offices, string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : offices.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));
}
