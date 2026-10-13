using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using PoolCar.Reservations.Data;
using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Endpoints;

public static class ReservationEndpoints
{
    public const int HistoryDays = 30;

    public static IEndpointRouteBuilder MapReservationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reservations").WithTags("Reservations");

        group.MapPost("", CreateReservation)
            .WithName("CreateReservation")
            .WithSummary("Reserve a car")
            .WithDescription(
                "Reserves a car for the acting employee for a day part on every date from `from` to `to`. " +
                "A refused booking returns 409 with the `conflictingDates`.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/now", BookNow)
            .WithName("BookNow")
            .WithSummary("Book a car now and pick it up")
            .WithDescription(
                "Reserves a car from now until the end of the current day part and picks it up immediately. " +
                "Outside day-part hours this returns 422.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/mine", GetMyReservations)
            .WithName("GetMyReservations")
            .WithSummary("List my reservations")
            .WithDescription(
                $"Returns the acting employee's reservations of the last {HistoryDays} days and the future.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", GetReservation)
            .WithName("GetReservation")
            .WithSummary("Get a reservation")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/cancel", CancelReservation)
            .WithName("CancelReservation")
            .WithSummary("Cancel a reservation")
            .WithDescription(
                "Cancels every period that has not been picked up. Employees can cancel their own reservations; " +
                "operators can cancel any reservation. Returns 409 when nothing is left to cancel.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<Results<Created<ReservationResponse>, ProblemHttpResult>> CreateReservation(
        CreateReservationRequest request,
        [AsParameters] ActingUserHeaders headers,
        ReservationsDbContext db,
        ReservationStore store,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (headers.ToActingUser() is not { } user)
        {
            return ApiProblems.Unauthorized();
        }

        var now = time.GetUtcNow();
        var created = Reservation.Create(
            user.EmployeeId,
            request.CarId?.Trim() ?? string.Empty,
            request.From,
            request.To,
            request.DayPart,
            request.Source ?? ReservationSource.User,
            now);

        return created.IsSuccess
            ? await AddAsync(created.Value, db, store, now, cancellationToken)
            : ApiProblems.FromError(created.Error);
    }

    private static async Task<Results<Created<ReservationResponse>, ProblemHttpResult>> BookNow(
        BookNowRequest request,
        [AsParameters] ActingUserHeaders headers,
        ReservationsDbContext db,
        ReservationStore store,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (headers.ToActingUser() is not { } user)
        {
            return ApiProblems.Unauthorized();
        }

        var now = time.GetUtcNow();
        var created = Reservation.BookNow(
            user.EmployeeId,
            request.CarId?.Trim() ?? string.Empty,
            request.Source ?? ReservationSource.User,
            now);

        return created.IsSuccess
            ? await AddAsync(created.Value, db, store, now, cancellationToken)
            : ApiProblems.FromError(created.Error);
    }

    private static async Task<Results<Created<ReservationResponse>, ProblemHttpResult>> AddAsync(
        Reservation reservation,
        ReservationsDbContext db,
        ReservationStore store,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == reservation.EmployeeId, cancellationToken))
        {
            return ApiProblems.Invalid("Onbekende medewerker.");
        }

        var today = DayParts.DateOf(now);
        if (reservation.FromDate <= today && today <= reservation.ToDate
            && await store.IsOverdueAsync(reservation.CarId, now, cancellationToken))
        {
            return ApiProblems.FromError(
                DomainError.Conflict("De auto is nog niet teruggebracht."),
                [today]);
        }

        var added = await store.AddAsync(reservation, cancellationToken);
        if (!added.IsSuccess)
        {
            var conflictingDates = added.Error.Code == DomainError.ConflictCode
                ? await store.FindConflictsAsync(reservation, cancellationToken)
                : [];
            return ApiProblems.FromError(added.Error, conflictingDates);
        }

        return TypedResults.Created(
            $"/reservations/{reservation.Id}",
            ReservationResponse.Create(reservation, now));
    }

    private static async Task<Results<Ok<IReadOnlyList<ReservationResponse>>, ProblemHttpResult>> GetMyReservations(
        [AsParameters] ActingUserHeaders headers,
        ReservationsDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (headers.ToActingUser() is not { } user)
        {
            return ApiProblems.Unauthorized();
        }

        var now = time.GetUtcNow();
        var since = DayParts.DateOf(now).AddDays(-HistoryDays);
        var reservations = await db.Reservations
            .AsNoTracking()
            .Include(r => r.Periods)
            .Where(r => r.EmployeeId == user.EmployeeId && r.ToDate >= since)
            .OrderBy(r => r.FromDate)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<ReservationResponse>>(
            [.. reservations.Select(r => ReservationResponse.Create(r, now))]);
    }

    private static async Task<Results<Ok<ReservationResponse>, ProblemHttpResult>> GetReservation(
        Guid id,
        ReservationsDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var reservation = await db.Reservations
            .AsNoTracking()
            .Include(r => r.Periods)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        return reservation is null
            ? ReservationNotFound()
            : TypedResults.Ok(ReservationResponse.Create(reservation, time.GetUtcNow()));
    }

    private static async Task<Results<Ok<ReservationResponse>, ProblemHttpResult>> CancelReservation(
        Guid id,
        [AsParameters] ActingUserHeaders headers,
        ReservationsDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (headers.ToActingUser() is not { } user)
        {
            return ApiProblems.Unauthorized();
        }

        var reservation = await db.Reservations
            .Include(r => r.Periods)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (reservation is null)
        {
            return ReservationNotFound();
        }

        if (!user.CanActOn(reservation.EmployeeId))
        {
            return ApiProblems.Forbidden();
        }

        var now = time.GetUtcNow();
        var cancelled = reservation.Cancel(now);
        if (!cancelled.IsSuccess)
        {
            return ApiProblems.StateConflict(cancelled.Error);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ReservationResponse.Create(reservation, now));
    }

    internal static ProblemHttpResult ReservationNotFound() =>
        ApiProblems.NotFound("Deze reservering bestaat niet.");
}
