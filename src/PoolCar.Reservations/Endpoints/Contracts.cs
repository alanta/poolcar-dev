using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Endpoints;

public sealed record EmployeeRequest(string Name, string HomeOffice);

public sealed record EmployeeResponse(Guid Id, string Name, string HomeOffice, EmployeeKind Kind)
{
    public static EmployeeResponse From(Employee employee) =>
        new(employee.Id, employee.Name, employee.HomeOffice, employee.Kind);
}

public sealed record CreateReservationRequest(
    string CarId,
    DateOnly From,
    DateOnly To,
    DayPart DayPart);

public sealed record BookNowRequest(string CarId);

public sealed record PeriodResponse(
    Guid Id,
    DateOnly Date,
    DateTimeOffset Start,
    DateTimeOffset End,
    PeriodStatus Status,
    DateTimeOffset? PickedUpAt,
    DateTimeOffset? ReturnedAt,
    bool NotPickedUp,
    bool Overdue)
{
    public static PeriodResponse Create(ReservationPeriod period, DateTimeOffset now) => new(
        period.Id,
        period.Date,
        period.Start,
        period.End,
        period.Status,
        period.PickedUpAt,
        period.ReturnedAt,
        period.IsNotPickedUp(now),
        period.IsOverdue(now));
}

public sealed record ReservationResponse(
    Guid Id,
    Guid EmployeeId,
    string CarId,
    DateOnly From,
    DateOnly To,
    DayPart DayPart,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CancelledAt,
    bool NotPickedUp,
    bool Overdue,
    IReadOnlyList<PeriodResponse> Periods)
{
    public static ReservationResponse Create(Reservation reservation, DateTimeOffset now)
    {
        var periods = reservation.Periods
            .OrderBy(p => p.Start)
            .Select(p => PeriodResponse.Create(p, now))
            .ToList();

        return new ReservationResponse(
            reservation.Id,
            reservation.EmployeeId,
            reservation.CarId,
            reservation.FromDate,
            reservation.ToDate,
            reservation.DayPart,
            reservation.CreatedAt,
            reservation.CancelledAt,
            periods.Any(p => p.NotPickedUp),
            periods.Any(p => p.Overdue),
            periods);
    }
}

public sealed record BusyCarsRequest(IReadOnlyList<string> CarIds, DateOnly From, DateOnly To, DayPart DayPart);

public sealed record CarsNowRequest(IReadOnlyList<string> CarIds);

public sealed record CarIdsResponse(IReadOnlyList<string> CarIds);

public sealed record ScheduleRequest(IReadOnlyList<string> CarIds, DateOnly From, DateOnly To);

public sealed record ScheduleEntryResponse(
    Guid ReservationId,
    Guid PeriodId,
    string CarId,
    DateOnly Date,
    DayPart DayPart,
    DateTimeOffset Start,
    DateTimeOffset End,
    PeriodStatus Status,
    string EmployeeFirstName,
    bool NotPickedUp,
    bool Overdue,
    bool AtRisk);
