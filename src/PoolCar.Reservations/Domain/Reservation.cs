namespace PoolCar.Reservations.Domain;

/// <summary>A car reserved by an employee for one or more dates.</summary>
public sealed class Reservation
{
    private readonly List<ReservationPeriod> _periods = [];

    private Reservation()
    {
        CarId = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid EmployeeId { get; private set; }

    /// <summary>The stored plate of the car, such as <c>HPN72V</c>.</summary>
    public string CarId { get; private set; }

    public DateOnly FromDate { get; private set; }

    public DateOnly ToDate { get; private set; }

    public DayPart DayPart { get; private set; }

    public ReservationSource Source { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public IReadOnlyList<ReservationPeriod> Periods => _periods;

    /// <summary>Cancels every period that has not been picked up yet.</summary>
    public Result Cancel(DateTimeOffset now)
    {
        var cancelled = 0;
        foreach (var period in _periods)
        {
            if (period.Cancel())
            {
                cancelled++;
            }
        }

        if (cancelled == 0)
        {
            return DomainError.NotAllowed("Er valt niets meer te annuleren. Breng de auto eerst terug.");
        }

        CancelledAt = now;
        return Result.Success();
    }
}
