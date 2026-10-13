using PoolCar.Web.Clients;
using PoolCar.Web.Formatting;
using PoolCar.Web.Schedule;

namespace PoolCar.Web.Desk;

/// <summary>A period on the desk board with its car.</summary>
public sealed record DeskItem(ScheduleEntry Period, FleetCar Car)
{
    /// <summary>The car can be handed out now: the period is scheduled and running.</summary>
    public bool CanPickUp { get; init; }

    /// <summary>Upcoming bookings of the car that are at risk because it is overdue.</summary>
    public IReadOnlyList<ScheduleEntry> AtRisk { get; init; } = [];

    /// <summary>"Ochtend 08:00–12:00".</summary>
    public string TimeText => $"{DayPartText.Label(Period.DayPart)} {DayPartText.Hours(Period.DayPart)}";

    public string StartTime => DayPartText.Start(Period.DayPart).ToString("HH:mm", DutchDate.Culture);

    public string ReturnTime => DayPartText.End(Period.DayPart).ToString("HH:mm", DutchDate.Culture);

    public string CarText => $"{Car.Make} {Car.Model}";
}

/// <summary>The work of the desk for an office on a date.</summary>
/// <remarks>
/// Picked-up periods of the date that are not yet overdue are split by how soon they end:
/// <see cref="DueBack"/> holds those ending within <see cref="DueBackWindow"/> from now, <see cref="OnTheWay"/>
/// those ending later. A period is never in both. <see cref="Overdue"/> holds every overdue period, whatever its
/// date.
/// </remarks>
public sealed record DeskBoard(
    IReadOnlyList<DeskItem> PickUps,
    IReadOnlyList<DeskItem> OnTheWay,
    IReadOnlyList<DeskItem> DueBack,
    IReadOnlyList<DeskItem> Overdue,
    int ReturnedCount)
{
    public static readonly TimeSpan DueBackWindow = TimeSpan.FromHours(2);

    public static DeskBoard Empty { get; } = new([], [], [], [], 0);

    /// <summary>Cars of the date that are out and not overdue.</summary>
    public int OutCount => OnTheWay.Count + DueBack.Count;

    public int OverdueCount => Overdue.Count;

    public static DeskBoard Build(ScheduleView schedule, DateOnly date, DateTimeOffset now)
    {
        var periods = schedule.Rows
            .SelectMany(row => row.Periods.Select(period => new DeskItem(period, row.Car)))
            .ToList();
        var onDate = periods.Where(i => i.Period.Date == date).OrderBy(i => i.Period.Start).ToList();
        var pickedUp = onDate.Where(i => i.Period.Status == PeriodStatus.PickedUp && !i.Period.Overdue).ToList();
        var atRisk = periods.Where(i => i.Period.AtRisk).OrderBy(i => i.Period.Start).ToLookup(i => i.Car.Plate);

        return new DeskBoard(
            [.. onDate
                .Where(i => i.Period.Status == PeriodStatus.Scheduled)
                .Select(i => i with { CanPickUp = !i.Period.NotPickedUp && i.Period.Start <= now && now < i.Period.End })],
            [.. pickedUp.Where(i => i.Period.End - now > DueBackWindow)],
            [.. pickedUp.Where(i => i.Period.End - now <= DueBackWindow)],
            [.. periods
                .Where(i => i.Period.Overdue)
                .OrderBy(i => i.Period.End)
                .Select(i => i with { AtRisk = [.. atRisk[i.Car.Plate].Select(r => r.Period)] })],
            onDate.Count(i => i.Period.Status == PeriodStatus.Returned));
    }
}
