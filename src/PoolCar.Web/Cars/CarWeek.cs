using PoolCar.Web.Clients;
using PoolCar.Web.Schedule;

namespace PoolCar.Web.Cars;

public enum SlotState
{
    Free,
    Reserved,
    PickedUp,
    Overdue,
    Mine,
}

/// <summary>A morning or afternoon of a day in the week strip.</summary>
public sealed record WeekSlot(DayPart DayPart, SlotState State)
{
    public string Label => CarWeek.Label(State);
}

public sealed record WeekDay(DateOnly Date, bool IsToday, WeekSlot Morning, WeekSlot Afternoon);

/// <summary>A coming period of a car, for the list on the car page.</summary>
public sealed record CarBooking(ScheduleEntry Period, bool IsMine)
{
    public string StatusLabel => Period switch
    {
        { Overdue: true } => "Te laat",
        { Status: PeriodStatus.PickedUp } => "Opgehaald",
        { NotPickedUp: true } => "Niet opgehaald",
        _ => "Gereserveerd",
    };
}

/// <summary>The occupancy of a car in a week from Monday to Sunday. Cells are placed by the period's date and day
/// part; a full-day period fills both halves.</summary>
public static class CarWeek
{
    public static DateOnly StartOfWeek(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public static IReadOnlyList<WeekDay> Create(ScheduleRow row, DateOnly today, IReadOnlySet<Guid> myReservationIds)
    {
        var monday = StartOfWeek(today);
        return [.. Enumerable.Range(0, 7).Select(monday.AddDays).Select(date => new WeekDay(
            date,
            date == today,
            new WeekSlot(DayPart.Morning, StateOf(row.PrimaryAt(date, DayPart.Morning), myReservationIds)),
            new WeekSlot(DayPart.Afternoon, StateOf(row.PrimaryAt(date, DayPart.Afternoon), myReservationIds))))];
    }

    public static SlotState StateOf(ScheduleEntry? period, IReadOnlySet<Guid> myReservationIds) => period switch
    {
        null or { Status: PeriodStatus.Returned or PeriodStatus.Cancelled } => SlotState.Free,
        { Overdue: true } => SlotState.Overdue,
        _ when myReservationIds.Contains(period.ReservationId) => SlotState.Mine,
        { Status: PeriodStatus.PickedUp } => SlotState.PickedUp,
        _ => SlotState.Reserved,
    };

    public static string Label(SlotState state) => state switch
    {
        SlotState.Free => "Vrij",
        SlotState.Reserved => "Gereserveerd",
        SlotState.PickedUp => "Opgehaald",
        SlotState.Overdue => "Te laat",
        SlotState.Mine => "Jouw reservering",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    /// <summary>The periods that are not over yet, plus cars that were not returned, by start.</summary>
    public static IReadOnlyList<CarBooking> Upcoming(ScheduleRow row, DateTimeOffset now, IReadOnlySet<Guid> myReservationIds) =>
        [.. row.Periods
            .Where(p => p.Status is PeriodStatus.Scheduled or PeriodStatus.PickedUp && (p.End > now || p.Overdue))
            .OrderBy(p => p.Start)
            .Select(p => new CarBooking(p, myReservationIds.Contains(p.ReservationId)))];
}
