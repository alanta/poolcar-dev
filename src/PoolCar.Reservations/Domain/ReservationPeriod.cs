namespace PoolCar.Reservations.Domain;

/// <summary>The time a car is reserved on a single date.</summary>
public sealed class ReservationPeriod
{
    private ReservationPeriod()
    {
    }

    private ReservationPeriod(DateOnly date, DateTimeOffset start, DateTimeOffset end)
    {
        Id = Guid.NewGuid();
        Date = date;
        Start = start;
        End = end;
        Status = PeriodStatus.Scheduled;
    }

    public Guid Id { get; private set; }

    public DateOnly Date { get; private set; }

    public DateTimeOffset Start { get; private set; }

    public DateTimeOffset End { get; private set; }

    public PeriodStatus Status { get; private set; }

    public DateTimeOffset? PickedUpAt { get; private set; }

    public DateTimeOffset? ReturnedAt { get; private set; }

    /// <summary>Creates a scheduled period.</summary>
    public static ReservationPeriod Schedule(DateOnly date, DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            throw new ArgumentException("The end of a period must be after its start.", nameof(end));
        }

        return new ReservationPeriod(date, start, end);
    }

    /// <summary>The period was never picked up and has ended.</summary>
    public bool IsNotPickedUp(DateTimeOffset now) => Status == PeriodStatus.Scheduled && End <= now;

    /// <summary>The car was picked up and not returned before the period ended.</summary>
    public bool IsOverdue(DateTimeOffset now) => Status == PeriodStatus.PickedUp && End <= now;

    /// <summary>Whether the period keeps the car from being reserved by someone else.</summary>
    public bool BlocksCar => Status is PeriodStatus.Scheduled or PeriodStatus.PickedUp;

    public bool Overlaps(DateTimeOffset start, DateTimeOffset end) => Start < end && start < End;

    public Result PickUp(DateTimeOffset now)
    {
        if (Status != PeriodStatus.Scheduled)
        {
            return DomainError.NotAllowed("Deze reservering kan je niet ophalen.");
        }

        if (now < Start)
        {
            return DomainError.NotAllowed("Je kan de auto pas ophalen als je reservering begint.");
        }

        if (now >= End)
        {
            return DomainError.NotAllowed("Je reservering is al voorbij.");
        }

        Status = PeriodStatus.PickedUp;
        PickedUpAt = now;
        return Result.Success();
    }

    public Result Return(DateTimeOffset now)
    {
        if (Status != PeriodStatus.PickedUp)
        {
            return DomainError.NotAllowed("Je kan alleen een opgehaalde auto terugbrengen.");
        }

        if (now < End)
        {
            End = now;
        }

        Status = PeriodStatus.Returned;
        ReturnedAt = now;
        return Result.Success();
    }

    internal bool Cancel()
    {
        if (Status != PeriodStatus.Scheduled)
        {
            return false;
        }

        Status = PeriodStatus.Cancelled;
        return true;
    }
}
