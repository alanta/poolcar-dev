using Microsoft.EntityFrameworkCore;
using Npgsql;
using PoolCar.Reservations.Data.Configurations;
using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Data;

/// <summary>Saves reservations without double-booking a car.</summary>
public sealed class ReservationStore(ReservationsDbContext db)
{
    /// <summary>Saves a new reservation, or returns a conflict when its car is already reserved.</summary>
    public async Task<Result> AddAsync(Reservation reservation, CancellationToken cancellationToken = default)
    {
        var available = await EnsureAvailableAsync(reservation, cancellationToken);
        if (!available.IsSuccess)
        {
            return available;
        }

        db.Reservations.Add(reservation);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ExclusionViolation,
        })
        {
            Detach(reservation);

            // Another reservation for the car was saved after the check above.
            var conflict = await EnsureAvailableAsync(reservation, cancellationToken);
            return conflict.IsSuccess ? DomainError.Conflict("De auto is al gereserveerd.") : conflict;
        }

        return Result.Success();
    }

    /// <summary>Returns the dates of the reservation on which its car is already reserved.</summary>
    public async Task<IReadOnlyList<DateOnly>> FindConflictsAsync(
        Reservation reservation,
        CancellationToken cancellationToken = default) =>
        AvailabilityPolicy.FindConflicts(
            await LoadCarPeriodsAsync(reservation, cancellationToken),
            reservation.Periods);

    /// <summary>Whether the car was picked up and not returned before its period ended.</summary>
    public Task<bool> IsOverdueAsync(string carId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        db.ReservationPeriods
            .Where(p => EF.Property<string>(p, ReservationPeriodConfiguration.CarId) == carId)
            .AnyAsync(p => p.Status == PeriodStatus.PickedUp && p.End <= now, cancellationToken);

    private async Task<Result> EnsureAvailableAsync(Reservation reservation, CancellationToken cancellationToken) =>
        AvailabilityPolicy.EnsureAvailable(
            await LoadCarPeriodsAsync(reservation, cancellationToken),
            reservation.Periods);

    private async Task<List<ReservationPeriod>> LoadCarPeriodsAsync(
        Reservation reservation,
        CancellationToken cancellationToken)
    {
        var from = reservation.Periods.Min(p => p.Start);
        var to = reservation.Periods.Max(p => p.End);

        return await db.ReservationPeriods
            .AsNoTracking()
            .Where(p => EF.Property<string>(p, ReservationPeriodConfiguration.CarId) == reservation.CarId)
            .Where(p => p.Status == PeriodStatus.Scheduled || p.Status == PeriodStatus.PickedUp)
            .Where(p => p.Start < to && from < p.End)
            .ToListAsync(cancellationToken);
    }

    private void Detach(Reservation reservation)
    {
        // Detach the reservation first, so its periods stay in the reservation when they are detached.
        db.Entry(reservation).State = EntityState.Detached;
        foreach (var period in reservation.Periods.ToList())
        {
            db.Entry(period).State = EntityState.Detached;
        }
    }
}
