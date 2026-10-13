using PoolCar.Web.Clients;
using PoolCar.Web.Formatting;
using PoolCar.Web.Schedule;

namespace PoolCar.Web.Planning;

public enum SlotState
{
    Free,
    Reserved,
    PickedUp,
    Returned,
    Overdue,
}

/// <summary>A half day of a car in the planning grid: the morning or the afternoon of a date.</summary>
public sealed record PlanningSlot(DateOnly Date, DayPart Half, SlotState State, bool IsMine, bool IsPast, ScheduleEntry? Entry)
{
    public bool IsFree => State == SlotState.Free;

    /// <summary>Only free half days that have not ended can be reserved.</summary>
    public bool CanReserve => IsFree && !IsPast;

    /// <summary>Whether the period fills the whole date, so the morning and the afternoon look like one block.</summary>
    public bool IsFullDay => !IsFree && Entry?.DayPart == DayPart.FullDay;

    /// <summary>The value of <c>data-state</c> and the CSS modifier.</summary>
    public string StateName => State switch
    {
        SlotState.Free => "vrij",
        SlotState.Reserved => "gereserveerd",
        SlotState.PickedUp => "opgehaald",
        SlotState.Returned => "ingeleverd",
        SlotState.Overdue => "te-laat",
        _ => throw new ArgumentOutOfRangeException(nameof(State), State, null),
    };

    /// <summary>The text for screen readers, such as "Gereserveerd door Bram".</summary>
    public string Label
    {
        get
        {
            var who = IsMine ? "jou" : Entry?.EmployeeFirstName;
            return State switch
            {
                SlotState.Free => IsPast ? "Vrij, voorbij" : "Vrij",
                SlotState.Reserved => $"Gereserveerd door {who}",
                SlotState.PickedUp => $"Opgehaald door {who}",
                SlotState.Returned => $"Ingeleverd door {who}",
                SlotState.Overdue => $"Te laat, nog bij {who}",
                _ => throw new ArgumentOutOfRangeException(nameof(State), State, null),
            };
        }
    }

    /// <summary>The tooltip: who and the period, such as "Bram, di 13 okt, hele dag 08:00–18:00". The hours come
    /// from the day part.</summary>
    public string Title => Entry is null
        ? $"{(CanReserve ? "Vrij, reserveer" : "Vrij")}: {Period(Date, Half)}"
        : $"{Entry.EmployeeFirstName}{(IsMine ? " (jij)" : string.Empty)}, {Period(Entry.Date, Entry.DayPart)}";

    private static string Period(DateOnly date, DayPart dayPart) =>
        $"{DutchDate.Short(date)}, {DayPartText.Label(dayPart).ToLowerInvariant()} {DayPartText.Hours(dayPart)}";
}

public sealed record PlanningRow(FleetCar Car, IReadOnlyList<PlanningSlot> Slots);

/// <summary>Turns a schedule into rows of half-day slots. Periods are placed by their date and day part; a full-day
/// period fills both halves.</summary>
public static class PlanningGrid
{
    public static readonly IReadOnlyList<DayPart> Halves = [DayPart.Morning, DayPart.Afternoon];

    /// <summary>The Monday of the week of <paramref name="date"/>.</summary>
    public static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public static IReadOnlyList<PlanningRow> Build(
        ScheduleView schedule,
        IReadOnlySet<Guid> myReservationIds,
        DateTimeOffset now)
    {
        var localNow = AmsterdamTime.ToLocal(now).DateTime;
        return
        [
            .. schedule.Rows.Select(row => new PlanningRow(
                row.Car,
                [.. schedule.Dates.SelectMany(date => Halves.Select(half => CreateSlot(row, date, half, myReservationIds, localNow)))])),
        ];
    }

    private static PlanningSlot CreateSlot(
        ScheduleRow row,
        DateOnly date,
        DayPart half,
        IReadOnlySet<Guid> myReservationIds,
        DateTime localNow)
    {
        var isPast = date.ToDateTime(DayPartText.End(half)) <= localNow;
        var entry = row.PrimaryAt(date, half);
        var state = entry switch
        {
            null => SlotState.Free,
            { Overdue: true } => SlotState.Overdue,
            { Status: PeriodStatus.PickedUp } => SlotState.PickedUp,
            { Status: PeriodStatus.Scheduled } => SlotState.Reserved,
            { Status: PeriodStatus.Returned } when isPast => SlotState.Returned,

            // A car that was returned early is free again for the rest of its period
            _ => SlotState.Free,
        };

        if (state == SlotState.Free)
        {
            entry = null;
        }

        var isMine = entry is not null && myReservationIds.Contains(entry.ReservationId);
        return new PlanningSlot(date, half, state, isMine, isPast, entry);
    }
}
