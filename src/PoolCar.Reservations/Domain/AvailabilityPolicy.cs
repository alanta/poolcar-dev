namespace PoolCar.Reservations.Domain;

/// <summary>Decides whether a car can be reserved.</summary>
public static class AvailabilityPolicy
{
    /// <summary>Returns the dates of the requested periods that overlap a blocking period of the car.</summary>
    public static IReadOnlyList<DateOnly> FindConflicts(
        IEnumerable<ReservationPeriod> carPeriods,
        IEnumerable<ReservationPeriod> requested)
    {
        var blocking = carPeriods.Where(p => p.BlocksCar).ToList();

        return requested
            .Where(r => blocking.Any(b => b.Id != r.Id && b.Overlaps(r.Start, r.End)))
            .Select(r => r.Date)
            .Distinct()
            .Order()
            .ToList();
    }

    /// <summary>Returns a conflict error when any of the requested periods is unavailable.</summary>
    public static Result EnsureAvailable(
        IEnumerable<ReservationPeriod> carPeriods,
        IEnumerable<ReservationPeriod> requested)
    {
        var conflicts = FindConflicts(carPeriods, requested);
        if (conflicts.Count == 0)
        {
            return Result.Success();
        }

        var dates = string.Join(", ", conflicts.Select(d => d.ToString("dd-MM-yyyy", null)));
        return DomainError.Conflict($"De auto is al gereserveerd op {dates}.");
    }

    /// <summary>Whether the car has not been returned after a period ended.</summary>
    public static bool IsBlockedToday(IEnumerable<ReservationPeriod> carPeriods, DateTimeOffset now) =>
        carPeriods.Any(p => p.IsOverdue(now));

    /// <summary>Whether an upcoming period is at risk because its car has not been returned.</summary>
    public static bool IsAtRisk(
        ReservationPeriod period,
        IEnumerable<ReservationPeriod> carPeriods,
        DateTimeOffset now) =>
        period.Status == PeriodStatus.Scheduled
        && period.End > now
        && carPeriods.Any(p => p.Id != period.Id && p.IsOverdue(now));
}
