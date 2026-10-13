namespace PoolCar.Reservations.Domain;

/// <summary>A car reserved by an employee for one or more dates.</summary>
public sealed class Reservation
{
    public const int MaxDays = 14;

    private readonly List<ReservationPeriod> _periods = [];

    private Reservation()
    {
        CarId = string.Empty;
    }

    private Reservation(
        Guid employeeId,
        string carId,
        DateOnly fromDate,
        DateOnly toDate,
        DayPart dayPart,
        DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        EmployeeId = employeeId;
        CarId = carId;
        FromDate = fromDate;
        ToDate = toDate;
        DayPart = dayPart;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid EmployeeId { get; private set; }

    /// <summary>The stored plate of the car, such as <c>HPN72V</c>.</summary>
    public string CarId { get; private set; }

    public DateOnly FromDate { get; private set; }

    public DateOnly ToDate { get; private set; }

    public DayPart DayPart { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public IReadOnlyList<ReservationPeriod> Periods => _periods;

    /// <summary>Creates a reservation with one period for every date from <paramref name="fromDate"/> to
    /// <paramref name="toDate"/>.</summary>
    public static Result<Reservation> Create(
        Guid employeeId,
        string carId,
        DateOnly fromDate,
        DateOnly toDate,
        DayPart dayPart,
        DateTimeOffset now)
    {
        var error = Validate(employeeId, carId, dayPart);
        if (error is not null)
        {
            return error;
        }

        if (fromDate > toDate)
        {
            return DomainError.Invalid("De einddatum ligt voor de begindatum.");
        }

        if (toDate.DayNumber - fromDate.DayNumber + 1 > MaxDays)
        {
            return DomainError.Invalid($"Je kan maximaal {MaxDays} dagen reserveren.");
        }

        var reservation = new Reservation(employeeId, carId, fromDate, toDate, dayPart, now);
        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            var (start, end) = DayParts.ToPeriod(date, dayPart);
            reservation._periods.Add(ReservationPeriod.Schedule(date, start, end));
        }

        return reservation;
    }

    /// <summary>Creates a reservation that starts now, ends with the current day part and is picked up
    /// immediately.</summary>
    public static Result<Reservation> BookNow(
        Guid employeeId,
        string carId,
        DateTimeOffset now)
    {
        if (DayParts.CurrentDayPart(now) is not { } dayPart)
        {
            return DomainError.Invalid("Nu kan je geen auto boeken. Plan vooruit.");
        }

        var error = Validate(employeeId, carId, dayPart);
        if (error is not null)
        {
            return error;
        }

        var date = DayParts.DateOf(now);
        var (_, end) = DayParts.ToPeriod(date, dayPart);
        var start = new DateTimeOffset(now.Ticks - (now.Ticks % TimeSpan.TicksPerMinute), now.Offset);

        var period = ReservationPeriod.Schedule(date, start, end);
        var pickUp = period.PickUp(now);
        if (!pickUp.IsSuccess)
        {
            return pickUp.Error;
        }

        var reservation = new Reservation(employeeId, carId, date, date, dayPart, now);
        reservation._periods.Add(period);
        return reservation;
    }

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
            return DomainError.NotAllowed("Er valt niets meer te annuleren.");
        }

        CancelledAt = now;
        return Result.Success();
    }

    private static DomainError? Validate(Guid employeeId, string carId, DayPart dayPart)
    {
        if (employeeId == Guid.Empty)
        {
            return DomainError.Invalid("Onbekende medewerker.");
        }

        if (string.IsNullOrWhiteSpace(carId))
        {
            return DomainError.Invalid("Kies een auto.");
        }

        if (!Enum.IsDefined(dayPart))
        {
            return DomainError.Invalid("Kies een dagdeel.");
        }

        return null;
    }
}
