using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using PoolCar.Reservations.Data;
using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Endpoints;

public static class PeriodEndpoints
{
    public static IEndpointRouteBuilder MapPeriodEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/periods").WithTags("Periods");

        group.MapPost("/{id:guid}/pickup", PickUp)
            .WithName("PickUpPeriod")
            .WithSummary("Pick up a car")
            .WithDescription(
                "Picks up the car of a scheduled period while the period is running. Employees can pick up their " +
                "own reservations; operators can act on any reservation. Returns 409 when the period cannot be " +
                "picked up now.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/return", Return)
            .WithName("ReturnPeriod")
            .WithSummary("Return a car")
            .WithDescription(
                "Returns a picked-up car. An early return ends the period now, so the rest of it can be booked " +
                "again. Returns 409 when the car was not picked up.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static Task<Results<Ok<ReservationResponse>, ProblemHttpResult>> PickUp(
        Guid id,
        [AsParameters] ActingUserHeaders headers,
        ReservationsDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        ChangeAsync(id, headers, db, time, (period, now) => period.PickUp(now), cancellationToken);

    private static Task<Results<Ok<ReservationResponse>, ProblemHttpResult>> Return(
        Guid id,
        [AsParameters] ActingUserHeaders headers,
        ReservationsDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        ChangeAsync(id, headers, db, time, (period, now) => period.Return(now), cancellationToken);

    private static async Task<Results<Ok<ReservationResponse>, ProblemHttpResult>> ChangeAsync(
        Guid id,
        ActingUserHeaders headers,
        ReservationsDbContext db,
        TimeProvider time,
        Func<ReservationPeriod, DateTimeOffset, Result> change,
        CancellationToken cancellationToken)
    {
        if (headers.ToActingUser() is not { } user)
        {
            return ApiProblems.Unauthorized();
        }

        var reservation = await db.Reservations
            .Include(r => r.Periods)
            .FirstOrDefaultAsync(r => r.Periods.Any(p => p.Id == id), cancellationToken);
        if (reservation is null)
        {
            return ApiProblems.NotFound("Deze reservering bestaat niet.");
        }

        if (!user.CanActOn(reservation.EmployeeId))
        {
            return ApiProblems.Forbidden();
        }

        var now = time.GetUtcNow();
        var changed = change(reservation.Periods.Single(p => p.Id == id), now);
        if (!changed.IsSuccess)
        {
            return ApiProblems.StateConflict(changed.Error);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ReservationResponse.Create(reservation, now));
    }
}
