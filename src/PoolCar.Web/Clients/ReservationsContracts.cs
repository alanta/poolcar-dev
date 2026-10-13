namespace PoolCar.Web.Clients;

public enum DayPart
{
    Morning,
    Afternoon,
    FullDay,
}

public enum PeriodStatus
{
    Scheduled,
    PickedUp,
    Returned,
    Cancelled,
}

public enum ReservationSource
{
    User,
    Simulator,
}

public enum EmployeeKind
{
    Seeded,
    SignedIn,
}

/// <summary>An employee; <see cref="HomeOffice"/> is a Fleet office id such as <c>utrecht</c>.</summary>
public sealed record Employee(Guid Id, string Name, string HomeOffice, EmployeeKind Kind);

public sealed record EmployeeRequest(string Name, string HomeOffice);

public sealed record CreateReservationRequest(
    string CarId,
    DateOnly From,
    DateOnly To,
    DayPart DayPart,
    ReservationSource? Source = null);

public sealed record BookNowRequest(string CarId, ReservationSource? Source = null);

public sealed record ReservationPeriod(
    Guid Id,
    DateOnly Date,
    DateTimeOffset Start,
    DateTimeOffset End,
    PeriodStatus Status,
    DateTimeOffset? PickedUpAt,
    DateTimeOffset? ReturnedAt,
    bool NotPickedUp,
    bool Overdue);

public sealed record Reservation(
    Guid Id,
    Guid EmployeeId,
    string CarId,
    DateOnly From,
    DateOnly To,
    DayPart DayPart,
    ReservationSource Source,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CancelledAt,
    bool NotPickedUp,
    bool Overdue,
    IReadOnlyList<ReservationPeriod> Periods);

public sealed record BusyCarsRequest(IReadOnlyList<string> CarIds, DateOnly From, DateOnly To, DayPart DayPart);

public sealed record CarsNowRequest(IReadOnlyList<string> CarIds);

public sealed record CarIdsResponse(IReadOnlyList<string> CarIds);

public sealed record ScheduleRequest(IReadOnlyList<string> CarIds, DateOnly From, DateOnly To);

/// <summary>A period in the schedule. Place it in a grid by <see cref="Date"/> and <see cref="DayPart"/>.</summary>
public sealed record ScheduleEntry(
    Guid ReservationId,
    Guid PeriodId,
    string CarId,
    DateOnly Date,
    DayPart DayPart,
    DateTimeOffset Start,
    DateTimeOffset End,
    PeriodStatus Status,
    ReservationSource Source,
    string EmployeeFirstName,
    bool NotPickedUp,
    bool Overdue,
    bool AtRisk);
