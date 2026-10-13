using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using PoolCar.Reservations.Data;
using PoolCar.Reservations.Data.Configurations;
using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Endpoints;

public static class ScheduleEndpoints
{
    public const int MaxDays = 62;

    public static IEndpointRouteBuilder MapScheduleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/schedule", GetSchedule)
            .WithTags("Schedule")
            .WithName("GetSchedule")
            .WithSummary("Get the schedule of cars")
            .WithDescription(
                "Returns every period that is not cancelled for the cars on the dates from `from` to `to`, with " +
                "the flags `notPickedUp`, `overdue` and `atRisk` (an upcoming period whose car was not returned " +
                $"in time). At most {AvailabilityEndpoints.MaxCarsPerRequest} cars and {MaxDays} days. This is a " +
                "read-only query; it uses POST to keep URLs short and avoid encoding issues.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<Results<Ok<IReadOnlyList<ScheduleEntryResponse>>, ProblemHttpResult>> GetSchedule(
        ScheduleRequest request,
        ReservationsDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var carIds = AvailabilityEndpoints.NormaliseCarIds(request.CarIds);
        if (!carIds.IsSuccess)
        {
            return ApiProblems.FromError(carIds.Error);
        }

        if (request.From > request.To)
        {
            return ApiProblems.Invalid("De einddatum ligt voor de begindatum.");
        }

        if (request.To.DayNumber - request.From.DayNumber + 1 > MaxDays)
        {
            return ApiProblems.Invalid($"Je kan maximaal {MaxDays} dagen tegelijk opvragen.");
        }

        var now = time.GetUtcNow();
        var ids = carIds.Value;
        var entries = await (
                from period in db.ReservationPeriods.AsNoTracking()
                join reservation in db.Reservations
                    on EF.Property<Guid>(period, ReservationPeriodConfiguration.ReservationId) equals reservation.Id
                join employee in db.Employees on reservation.EmployeeId equals employee.Id
                where ids.Contains(reservation.CarId)
                    && period.Date >= request.From
                    && period.Date <= request.To
                    && period.Status != PeriodStatus.Cancelled
                orderby reservation.CarId, period.Start
                select new
                {
                    ReservationId = reservation.Id,
                    reservation.CarId,
                    reservation.DayPart,
                    EmployeeName = employee.Name,
                    Period = period,
                })
            .ToListAsync(cancellationToken);

        var overdue = await db.ReservationPeriods
            .AsNoTracking()
            .Where(p => ids.Contains(EF.Property<string>(p, ReservationPeriodConfiguration.CarId)))
            .Where(p => p.Status == PeriodStatus.PickedUp && p.End <= now)
            .Select(p => new { CarId = EF.Property<string>(p, ReservationPeriodConfiguration.CarId), Period = p })
            .ToListAsync(cancellationToken);
        var overdueByCar = overdue.ToLookup(p => p.CarId, p => p.Period);

        return TypedResults.Ok<IReadOnlyList<ScheduleEntryResponse>>(
        [
            .. entries.Select(e => new ScheduleEntryResponse(
                e.ReservationId,
                e.Period.Id,
                e.CarId,
                e.Period.Date,
                e.DayPart,
                e.Period.Start,
                e.Period.End,
                e.Period.Status,
                FirstName(e.EmployeeName),
                e.Period.IsNotPickedUp(now),
                e.Period.IsOverdue(now),
                AvailabilityPolicy.IsAtRisk(e.Period, overdueByCar[e.CarId], now))),
        ]);
    }

    private static string FirstName(string name) =>
        name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
}
