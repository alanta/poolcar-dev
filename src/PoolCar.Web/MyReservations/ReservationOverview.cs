using PoolCar.Web.Clients;
using PoolCar.Web.Formatting;

namespace PoolCar.Web.MyReservations;

/// <summary>The group a reservation is shown in on Mijn reserveringen.</summary>
public enum ReservationGroup
{
    OnTheRoad,
    Planned,
    Past,
}

public enum PeriodState
{
    Scheduled,
    ReadyForPickUp,
    PickedUp,
    Overdue,
    NotPickedUp,
    Returned,
    Cancelled,
}

/// <summary>A period of a reservation as shown on its card.</summary>
public sealed record PeriodView(
    Guid Id,
    DateOnly Date,
    DateTimeOffset Start,
    string DateText,
    string Hours,
    PeriodState State,
    string? OverdueSince)
{
    public bool CanPickUp => State == PeriodState.ReadyForPickUp;

    public bool CanReturn => State is PeriodState.PickedUp or PeriodState.Overdue;
}

/// <summary>A reservation as a card: when, which car, its open periods and the actions it allows.</summary>
public sealed record ReservationCard(
    Guid Id,
    ReservationGroup Group,
    string When,
    string? CarName,
    string Plate,
    IReadOnlyList<PeriodView> Periods,
    string? Summary,
    DateOnly From,
    DateOnly To)
{
    /// <summary>"Skoda Elroq KSP-73-B", or "auto KSP-73-B" when the car is unknown.</summary>
    public string Car => $"{CarName ?? "auto"} {Plate}";

    /// <summary>"Di 13 en wo 14 oktober, ochtend, Skoda Elroq KSP-73-B".</summary>
    public string Title => $"{When}, {Car}";

    public bool IsOverdue => Periods.Any(p => p.State == PeriodState.Overdue);

    /// <summary>Cancelling cancels the periods that have not been picked up and have not ended.</summary>
    public bool CanCancel => Periods.Any(p => p.State is PeriodState.Scheduled or PeriodState.ReadyForPickUp);
}

/// <summary>The employee's reservations in three groups: on the road, planned and of the last 30 days.</summary>
public sealed record ReservationOverview(
    IReadOnlyList<ReservationCard> OnTheRoad,
    IReadOnlyList<ReservationCard> Planned,
    IReadOnlyList<ReservationCard> Past)
{
    public const int HistoryDays = 30;

    public static readonly ReservationOverview Empty = new([], [], []);

    public static ReservationOverview Create(
        IEnumerable<Reservation> reservations,
        IEnumerable<FleetCar> cars,
        DateTimeOffset now,
        DateOnly today)
    {
        var carsByPlate = cars
            .GroupBy(c => c.Plate, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var since = today.AddDays(-HistoryDays);
        var cards = reservations
            .Where(r => r.To >= since)
            .Select(r => CreateCard(r, carsByPlate.GetValueOrDefault(r.CarId), now, today))
            .ToList();

        return new ReservationOverview(
            [.. cards.Where(c => c.Group == ReservationGroup.OnTheRoad).OrderBy(FirstStart)],
            [.. cards.Where(c => c.Group == ReservationGroup.Planned).OrderBy(FirstStart)],
            [.. cards.Where(c => c.Group == ReservationGroup.Past).OrderByDescending(c => c.To).ThenByDescending(c => c.From)]);
    }

    public static ReservationCard CreateCard(Reservation reservation, FleetCar? car, DateTimeOffset now, DateOnly today)
    {
        var hours = DayPartText.Hours(reservation.DayPart);
        var all = reservation.Periods
            .OrderBy(p => p.Date)
            .ThenBy(p => p.Start)
            .Select(p =>
            {
                var state = GetState(p, now);
                return new PeriodView(
                    p.Id,
                    p.Date,
                    p.Start,
                    DutchDate.Capitalize(DutchDate.Short(p.Date)),
                    hours,
                    state,
                    state == PeriodState.Overdue ? OverdueSince(p, today) : null);
            })
            .ToList();

        var group = GetGroup(all, today);
        var open = group == ReservationGroup.Past
            ? []
            : all.Where(p => p.State is not (PeriodState.Returned or PeriodState.Cancelled)).ToList();
        var when = $"{DutchDate.Capitalize(DutchDate.Range(reservation.From, reservation.To))}, " +
            DayPartText.Label(reservation.DayPart).ToLower(DutchDate.Culture);

        return new ReservationCard(
            reservation.Id,
            group,
            when,
            car is null ? null : $"{car.Make} {car.Model}",
            LicensePlate.Format(reservation.CarId),
            open,
            group == ReservationGroup.Past ? Summarize(all) : null,
            reservation.From,
            reservation.To);
    }

    public static PeriodState GetState(ReservationPeriod period, DateTimeOffset now) => period.Status switch
    {
        PeriodStatus.Cancelled => PeriodState.Cancelled,
        PeriodStatus.Returned => PeriodState.Returned,
        PeriodStatus.PickedUp => period.Overdue || period.End <= now ? PeriodState.Overdue : PeriodState.PickedUp,
        _ when period.NotPickedUp || period.End <= now => PeriodState.NotPickedUp,
        _ when period.Start <= now => PeriodState.ReadyForPickUp,
        _ => PeriodState.Scheduled,
    };

    private static ReservationGroup GetGroup(IReadOnlyList<PeriodView> periods, DateOnly today)
    {
        if (periods.Any(p => p.CanReturn))
        {
            return ReservationGroup.OnTheRoad;
        }

        // A period that was not picked up stays visible under Gepland until the end of its date
        return periods.Any(p => p.State is PeriodState.Scheduled or PeriodState.ReadyForPickUp
                || (p.State == PeriodState.NotPickedUp && p.Date >= today))
            ? ReservationGroup.Planned
            : ReservationGroup.Past;
    }

    private static string OverdueSince(ReservationPeriod period, DateOnly today)
    {
        var time = AmsterdamTime.Time(period.End);
        return period.Date == today
            ? $"Te laat sinds {time}"
            : $"Te laat sinds {DutchDate.Short(period.Date)} {time}";
    }

    private static string Summarize(IReadOnlyList<PeriodView> periods)
    {
        var labels = new List<string>();
        if (periods.Any(p => p.State == PeriodState.Returned))
        {
            labels.Add("Ingeleverd");
        }

        if (periods.Any(p => p.State == PeriodState.NotPickedUp))
        {
            labels.Add("Niet opgehaald");
        }

        if (periods.Any(p => p.State == PeriodState.Cancelled))
        {
            labels.Add("Geannuleerd");
        }

        return labels.Count == 0 ? "Afgelopen" : string.Join(", ", labels);
    }

    private static DateTimeOffset FirstStart(ReservationCard card) =>
        card.Periods.Count == 0 ? DateTimeOffset.MaxValue : card.Periods.Min(p => p.Start);
}
