using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using PoolCar.Reservations.Data;
using PoolCar.Reservations.Data.Configurations;
using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Endpoints;

public static class AvailabilityEndpoints
{
    public const int MaxCarsPerRequest = 200;

    private const string ReadOnlyNote =
        "This is a read-only query; it uses POST to keep URLs short and avoid encoding issues.";

    public static IEndpointRouteBuilder MapAvailabilityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/availability").WithTags("Availability");

        group.MapPost("/busy", GetBusyCars)
            .WithName("GetBusyCars")
            .WithSummary("Find cars that are not free for a booking")
            .WithDescription(
                "Returns the ids of the cars that are not free for every requested period, including cars that " +
                $"were not returned in time when the request includes today. At most {MaxCarsPerRequest} cars. " +
                ReadOnlyNote)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/now", GetCarsInUse)
            .WithName("GetCarsInUse")
            .WithSummary("Find cars that are in use now")
            .WithDescription(
                "Returns the ids of the cars with a scheduled or picked-up period running now, or that were not " +
                $"returned in time. At most {MaxCarsPerRequest} cars. " + ReadOnlyNote)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    /// <summary>Returns the distinct, trimmed car ids, or an error when there are too many.</summary>
    internal static Result<IReadOnlyList<string>> NormaliseCarIds(IReadOnlyList<string>? carIds)
    {
        IReadOnlyList<string> ids = [
            .. (carIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(),
        ];

        return ids.Count > MaxCarsPerRequest
            ? DomainError.Invalid($"Je kan maximaal {MaxCarsPerRequest} auto's tegelijk opvragen.")
            : Result<IReadOnlyList<string>>.Success(ids);
    }

    private static async Task<Results<Ok<CarIdsResponse>, ProblemHttpResult>> GetBusyCars(
        BusyCarsRequest request,
        ReservationsDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var carIds = NormaliseCarIds(request.CarIds);
        if (!carIds.IsSuccess)
        {
            return ApiProblems.FromError(carIds.Error);
        }

        if (request.From > request.To)
        {
            return ApiProblems.Invalid("De einddatum ligt voor de begindatum.");
        }

        if (request.To.DayNumber - request.From.DayNumber + 1 > Reservation.MaxDays)
        {
            return ApiProblems.Invalid($"Je kan maximaal {Reservation.MaxDays} dagen reserveren.");
        }

        if (!Enum.IsDefined(request.DayPart))
        {
            return ApiProblems.Invalid("Kies een dagdeel.");
        }

        if (carIds.Value.Count == 0)
        {
            return TypedResults.Ok(new CarIdsResponse([]));
        }

        var now = time.GetUtcNow();
        var today = DayParts.DateOf(now);
        var includesToday = request.From <= today && today <= request.To;
        var requested = new List<ReservationPeriod>();
        for (var date = request.From; date <= request.To; date = date.AddDays(1))
        {
            var (start, end) = DayParts.ToPeriod(date, request.DayPart);
            requested.Add(ReservationPeriod.Schedule(date, start, end));
        }

        var from = requested.Min(p => p.Start);
        var to = requested.Max(p => p.End);
        var ids = carIds.Value;
        var periods = await db.ReservationPeriods
            .AsNoTracking()
            .Where(p => ids.Contains(EF.Property<string>(p, ReservationPeriodConfiguration.CarId)))
            .Where(p => p.Status == PeriodStatus.Scheduled || p.Status == PeriodStatus.PickedUp)
            .Where(p => (p.Start < to && from < p.End)
                || (includesToday && p.Status == PeriodStatus.PickedUp && p.End <= now))
            .Select(p => new { CarId = EF.Property<string>(p, ReservationPeriodConfiguration.CarId), Period = p })
            .ToListAsync(cancellationToken);

        var periodsByCar = periods.ToLookup(p => p.CarId, p => p.Period);
        var busy = ids
            .Where(id => AvailabilityPolicy.FindConflicts(periodsByCar[id], requested).Count > 0
                || (includesToday && AvailabilityPolicy.IsBlockedToday(periodsByCar[id], now)))
            .ToList();

        return TypedResults.Ok(new CarIdsResponse(busy));
    }

    private static async Task<Results<Ok<CarIdsResponse>, ProblemHttpResult>> GetCarsInUse(
        CarsNowRequest request,
        ReservationsDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var carIds = NormaliseCarIds(request.CarIds);
        if (!carIds.IsSuccess)
        {
            return ApiProblems.FromError(carIds.Error);
        }

        var now = time.GetUtcNow();
        var ids = carIds.Value;
        var inUse = await db.ReservationPeriods
            .AsNoTracking()
            .Where(p => ids.Contains(EF.Property<string>(p, ReservationPeriodConfiguration.CarId)))
            .Where(p => ((p.Status == PeriodStatus.Scheduled || p.Status == PeriodStatus.PickedUp)
                    && p.Start <= now && now < p.End)
                || (p.Status == PeriodStatus.PickedUp && p.End <= now))
            .Select(p => EF.Property<string>(p, ReservationPeriodConfiguration.CarId))
            .Distinct()
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new CarIdsResponse([.. ids.Where(inUse.Contains)]));
    }
}
