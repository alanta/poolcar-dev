using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using PoolCar.Web.Availability;
using PoolCar.Web.Clients;
using PoolCar.Web.Employees;
using PoolCar.Web.Formatting;
using PoolCar.Web.Htmx;
using PoolCar.Web.Offices;
using PoolCar.Web.Reserving;

namespace PoolCar.Web.Pages;

/// <summary>Reserves a car in steps: when and where, category, car and confirmation. A car in <c>auto</c>
/// without a <c>categorie</c> is preselected (from the car page or the planning), which skips the category and car
/// steps.</summary>
public class ReserverenModel(
    CurrentEmployee currentEmployee,
    FleetApiClient fleetApiClient,
    ReservationsApiClient reservationsApiClient,
    AvailabilityService availabilityService,
    OfficeDirectory officeDirectory,
    TimeProvider timeProvider) : PageModel
{
    public const string FormId = "reserve-form";
    public const string StepsPartial = "_ReserveSteps";
    public const string ConflictPartial = "_ReservationConflict";
    public const int VisibleCars = 6;

    [BindProperty(SupportsGet = true, Name = "vestiging")]
    public string? Office { get; set; }

    [BindProperty(SupportsGet = true, Name = "van")]
    public string? FromText { get; set; }

    [BindProperty(SupportsGet = true, Name = "tot")]
    public string? ToText { get; set; }

    [BindProperty(SupportsGet = true, Name = "dagdeel")]
    public string? DayPartSlug { get; set; }

    [BindProperty(SupportsGet = true, Name = "categorie")]
    public string? Category { get; set; }

    [BindProperty(SupportsGet = true, Name = "auto")]
    public string? Plate { get; set; }

    [BindProperty(SupportsGet = true, Name = "alle")]
    public bool ShowAll { get; set; }

    [BindProperty(SupportsGet = true, Name = "gereserveerd")]
    public Guid? ReservedId { get; set; }

    public IReadOnlyList<FleetOffice> Offices { get; private set; } = [];

    public FleetOffice? SelectedOffice { get; private set; }

    public DateOnly Today { get; private set; }

    public DateOnly From { get; private set; }

    public DateOnly To { get; private set; }

    public DayPart DayPart { get; private set; }

    /// <summary>"Di 13 en wo 14 oktober, ochtend, Utrecht", when the dates are valid.</summary>
    public string? Summary { get; private set; }

    public string? ValidationMessage { get; private set; }

    /// <summary>The value of the from field: the date as typed when it is not valid.</summary>
    public string FromValue => ValidationMessage is null || FromText is null ? Format(From) : FromText;

    public string ToValue => ValidationMessage is null || ToText is null ? Format(To) : ToText;

    public string? ErrorMessage { get; private set; }

    /// <summary>The car from <c>auto</c> when it was preselected, without choosing a category first.</summary>
    public FleetCar? PreselectedCar { get; private set; }

    public bool IsPreselected => PreselectedCar is not null;

    /// <summary>The car to confirm: free for the whole request.</summary>
    public FleetCar? SelectedCar { get; private set; }

    public string? SelectedCategoryName { get; private set; }

    public IReadOnlyList<CategoryTile> Categories { get; private set; } = [];

    public IReadOnlyList<CarCard> Cars { get; private set; } = [];

    public int HiddenCarCount { get; private set; }

    public ReservationConflict? Conflict { get; private set; }

    public ReservationConfirmation? Confirmation { get; private set; }

    public Toast? Toast { get; private set; }

    /// <summary>Moves focus to the deepest step after an htmx swap, except while the dates are being changed.</summary>
    public bool MoveFocus => Request.IsHtmxPartial() && Request.Headers["HX-Trigger"] != FormId;

    public string CategoryName(string categoryId) =>
        Categories.FirstOrDefault(c => c.Category.Id == categoryId)?.Category.Name ?? categoryId;

    public async Task<IActionResult> OnGetAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        if (ReservedId is { } reservedId && await LoadConfirmationAsync(reservedId, cancellationToken))
        {
            return Page();
        }

        await LoadAsync(cancellationToken);
        return StepsOrPage();
    }

    /// <summary>Reserves the selected car. A car that is taken, before posting or by a 409 from Reservations,
    /// shows <see cref="ConflictPartial"/> for a preselected car and for a car chosen from the list.</summary>
    public async Task<IActionResult> OnPostReserveAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        await LoadAsync(cancellationToken);
        if (Conflict is not null)
        {
            return StepsOrPage(StatusCodes.Status409Conflict);
        }

        if (SelectedCar is not { } car)
        {
            ErrorMessage ??= ValidationMessage is null ? "Kies eerst een auto." : null;
            return StepsOrPage(StatusCodes.Status422UnprocessableEntity);
        }

        var result = await reservationsApiClient.CreateReservationAsync(
            new CreateReservationRequest(car.Plate, From, To, DayPart),
            cancellationToken);

        if (result.IsSuccess)
        {
            var toast = Htmx.Toast.Success($"{car.Make} {car.Model} {LicensePlate.Format(car.Plate)} is gereserveerd.");
            if (!Request.IsHtmxPartial())
            {
                toast.Store(TempData);
                return Redirect(QueryHelpers.AddQueryString("/reserveren", "gereserveerd", result.Value.Id.ToString()));
            }

            Toast = toast;
            Confirmation = CreateConfirmation(car, result.Value.From, result.Value.DayPart);
            return StepsOrPage();
        }

        if (result.Problem.IsConflict)
        {
            // Reload the list, so it shows the cars that are still free, and replace the confirmation by the conflict
            await LoadAsync(cancellationToken);
            Conflict = ReservationConflict.Taken(result.Problem, CategoryStepUrl);
            SelectedCar = null;
            return StepsOrPage(StatusCodes.Status409Conflict);
        }

        ErrorMessage = result.Problem.Detail;
        return StepsOrPage(result.Problem.IsInvalid ? StatusCodes.Status422UnprocessableEntity : result.Problem.Status);
    }

    /// <summary>The URL of this page for the current request, with the given changes.</summary>
    public string StepUrl(
        string? office = null,
        string? category = null,
        string? plate = null,
        bool keepCategory = true,
        bool keepPlate = true,
        bool showAll = false)
    {
        var query = new List<KeyValuePair<string, string?>>
        {
            new("vestiging", office ?? SelectedOffice?.Id),
            new("van", Format(From)),
            new("tot", Format(To)),
            new("dagdeel", DayPartText.Slug(DayPart)),
            new("categorie", category ?? (keepCategory ? Category : null)),
            new("auto", plate ?? (keepPlate ? PreselectedCar?.Plate ?? Plate : null)),
            new("alle", showAll ? "true" : null),
        };

        return QueryHelpers.AddQueryString("/reserveren", query.Where(p => !string.IsNullOrEmpty(p.Value)));
    }

    /// <summary>The same request without the car, which shows the categories, or the cars of the chosen category.</summary>
    public string CategoryStepUrl => StepUrl(keepPlate: false);

    public static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private IActionResult StepsOrPage(int statusCode = StatusCodes.Status200OK)
    {
        if (Request.IsHtmxPartial())
        {
            return this.Partial(StepsPartial, this, statusCode);
        }

        Response.StatusCode = statusCode;
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        await currentEmployee.LoadAsync(cancellationToken);
        Offices = await officeDirectory.GetOfficesAsync(cancellationToken);
        Today = AmsterdamTime.Today(timeProvider);

        var car = await LoadCarAsync(cancellationToken);
        SelectedOffice = FindOffice(Office) ?? FindOffice(currentEmployee.HomeOffice) ?? Offices.FirstOrDefault();
        Office = SelectedOffice?.Id;

        if (!LoadDates() || SelectedOffice is null)
        {
            return;
        }

        Summary = ReservationDates.Summary(From, To, DayPart, SelectedOffice.Name);
        if (PreselectedCar is not null)
        {
            await LoadPreselectedCarAsync(PreselectedCar, cancellationToken);
            return;
        }

        var availability = await availabilityService.GetAvailableAsync(
            new AvailabilityRequest(SelectedOffice.Id, From, To, DayPart),
            cancellationToken);
        if (!availability.IsSuccess)
        {
            ErrorMessage = availability.Problem.Detail;
            return;
        }

        LoadCategories(availability.Value);
        if (Categories.All(c => c.Category.Id != Category))
        {
            Category = null;
            Plate = null;
            return;
        }

        var free = availability.Value.Cars
            .Where(c => c.Category == Category)
            .OrderByDescending(c => c.RangeKm)
            .ThenBy(c => c.Plate, StringComparer.Ordinal)
            .ToList();
        var visible = ShowAll || free.Count <= VisibleCars ? free : free.Take(VisibleCars).ToList();
        HiddenCarCount = free.Count - visible.Count;
        Cars = [.. visible.Select(c => new CarCard(c, StepUrl(plate: c.Plate), c.Plate == car?.Plate))];
        SelectedCategoryName = CategoryName(Category!);

        if (car is null)
        {
            return;
        }

        if (free.Any(c => c.Plate == car.Plate))
        {
            SelectedCar = car;
        }
        else
        {
            Conflict = await ReservationConflict.BusyAsync(car, From, To, DayPart, reservationsApiClient, CategoryStepUrl, cancellationToken);
        }
    }

    /// <summary>The car in <c>auto</c>. Without a category it is preselected and sets the office; with a category it
    /// must belong to that category and office.</summary>
    private async Task<FleetCar?> LoadCarAsync(CancellationToken cancellationToken)
    {
        var plate = Plate;
        if (string.IsNullOrEmpty(plate))
        {
            Plate = null;
            return null;
        }

        var car = await fleetApiClient.GetCarAsync(plate, cancellationToken);
        if (car is null)
        {
            ErrorMessage = $"Geen auto met kenteken {LicensePlate.Format(plate)} gevonden. Kies hieronder een auto.";
            Plate = null;
            return null;
        }

        if (string.IsNullOrEmpty(Category))
        {
            PreselectedCar = car;
            Office = car.Office;
            Plate = car.Plate;
            return car;
        }

        if (car.Category != Category || !string.Equals(car.Office, Office, StringComparison.OrdinalIgnoreCase))
        {
            Plate = null;
            return null;
        }

        Plate = car.Plate;
        return car;
    }

    private async Task LoadPreselectedCarAsync(FleetCar car, CancellationToken cancellationToken)
    {
        var busy = await reservationsApiClient.GetBusyCarIdsAsync([car.Plate], From, To, DayPart, cancellationToken);
        if (!busy.IsSuccess)
        {
            ErrorMessage = busy.Problem.Detail;
            return;
        }

        if (busy.Value.Contains(car.Plate, StringComparer.OrdinalIgnoreCase))
        {
            Conflict = await ReservationConflict.BusyAsync(car, From, To, DayPart, reservationsApiClient, CategoryStepUrl, cancellationToken);
            return;
        }

        SelectedCar = car;
    }

    private bool LoadDates()
    {
        var fromValid = TryParse(FromText, out var from);
        var toValid = TryParse(ToText, out var to);
        From = fromValid ? from : ReservationDates.DefaultFrom(timeProvider);
        To = toValid ? to : From;
        DayPart = DayPartText.TryParseSlug(DayPartSlug, out var dayPart)
            ? dayPart
            : ReservationDates.DefaultDayPart(From, timeProvider);

        if ((!fromValid && !string.IsNullOrWhiteSpace(FromText)) || (!toValid && !string.IsNullOrWhiteSpace(ToText)))
        {
            ValidationMessage = "Kies een geldige datum.";
            return false;
        }

        ValidationMessage = ReservationDates.Validate(From, To, Today);
        return ValidationMessage is null;
    }

    private void LoadCategories(AvailabilityResult availability)
    {
        var otherOffices = Offices.Where(o => o.Id != SelectedOffice?.Id).ToList();
        Categories =
        [
            .. availability.Categories.Select(c => new CategoryTile(
                c.Category,
                c.Available,
                c.Total,
                Example(availability.Cars, c.Category.Id),
                StepUrl(category: c.Category.Id, keepPlate: false),
                c.Category.Id == Category,
                [.. otherOffices.Select(o => new OfficeLink(o.Name, StepUrl(office: o.Id, category: c.Category.Id, keepPlate: false)))])),
        ];
    }

    private async Task<bool> LoadConfirmationAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        await currentEmployee.LoadAsync(cancellationToken);
        var reservation = await reservationsApiClient.GetReservationAsync(reservationId, cancellationToken);
        if (reservation is null || reservation.EmployeeId != currentEmployee.Id)
        {
            return false;
        }

        var car = await fleetApiClient.GetCarAsync(reservation.CarId, cancellationToken);
        if (car is null)
        {
            return false;
        }

        Offices = await officeDirectory.GetOfficesAsync(cancellationToken);
        Today = AmsterdamTime.Today(timeProvider);
        SelectedOffice = FindOffice(car.Office) ?? Offices.FirstOrDefault();
        Office = SelectedOffice?.Id;
        From = reservation.From;
        To = reservation.To;
        DayPart = reservation.DayPart;
        Summary = ReservationDates.Summary(From, To, DayPart, SelectedOffice?.Name ?? car.Office);
        Confirmation = CreateConfirmation(car, reservation.From, reservation.DayPart);
        return true;
    }

    private ReservationConfirmation CreateConfirmation(FleetCar car, DateOnly firstDate, DayPart dayPart) =>
        new(
            $"{car.Make} {car.Model}",
            LicensePlate.Format(car.Plate),
            Summary ?? string.Empty,
            $"Haal de sleutel op bij de balie in {FindOffice(car.Office)?.Name ?? car.Office}, vanaf {DayPartText.Start(dayPart):HH\\:mm} op {ReservationDates.DayAndMonth(firstDate)}.");

    private FleetOffice? FindOffice(string? id) =>
        Offices.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));

    private static string? Example(IEnumerable<FleetCar> cars, string category)
    {
        var models = cars
            .Where(c => c.Category == category)
            .Select(c => $"{c.Make} {c.Model}")
            .Distinct()
            .Take(2)
            .ToList();
        return models.Count == 0 ? null : $"bijv. {string.Join(", ", models)}";
    }

    private static bool TryParse(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    public sealed record CategoryTile(
        FleetCategory Category,
        int Available,
        int Total,
        string? Example,
        string Url,
        bool IsSelected,
        IReadOnlyList<OfficeLink> OtherOffices)
    {
        public bool HasAvailable => Available > 0;
    }

    public sealed record OfficeLink(string Name, string Url);

    public sealed record CarCard(FleetCar Car, string ChooseUrl, bool IsSelected);

    public sealed record ReservationConfirmation(string CarName, string Plate, string Summary, string NextStep);
}

/// <summary>A car that can't be reserved for the request, because it was taken before or while posting.</summary>
public sealed record ReservationConflict(string Message, string OtherCarsUrl)
{
    /// <summary>The car was taken while posting: Reservations returned 409.</summary>
    public static ReservationConflict Taken(ApiProblem problem, string otherCarsUrl) =>
        new(
            problem.ConflictingDates.Count > 0
                ? $"Deze auto is net door iemand anders gereserveerd voor {ReservationDates.List(problem.ConflictingDates)}."
                : problem.Detail,
            otherCarsUrl);

    /// <summary>The car is not free for the request; names the dates from its schedule.</summary>
    public static async Task<ReservationConflict> BusyAsync(
        FleetCar car,
        DateOnly from,
        DateOnly to,
        DayPart dayPart,
        ReservationsApiClient reservationsApiClient,
        string otherCarsUrl,
        CancellationToken cancellationToken)
    {
        var schedule = await reservationsApiClient.GetScheduleAsync([car.Plate], from, to, cancellationToken);
        var dates = schedule
            .Where(e => e.Status is PeriodStatus.Scheduled or PeriodStatus.PickedUp
                && ReservationDates.Overlaps(e.DayPart, dayPart))
            .Select(e => e.Date)
            .ToList();
        return new(
            dates.Count > 0
                ? $"Deze auto is al gereserveerd voor {ReservationDates.List(dates)}."
                : "Deze auto is niet vrij in deze periode.",
            otherCarsUrl);
    }
}
