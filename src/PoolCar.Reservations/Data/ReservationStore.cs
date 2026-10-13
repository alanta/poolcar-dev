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

    private async Task<Result> EnsureAvailableAsync(Reservation reservation, CancellationToken cancellationToken)
    {
        var from = reservation.Periods.Min(p => p.Start);
        var to = reservation.Periods.Max(p => p.End);

        var carPeriods = await db.ReservationPeriods
            .AsNoTracking()
            .Where(p => EF.Property<string>(p, ReservationPeriodConfiguration.CarId) == reservation.CarId)
            .Where(p => p.Status == PeriodStatus.Scheduled || p.Status == PeriodStatus.PickedUp)
            .Where(p => p.Start < to && from < p.End)
            .ToListAsync(cancellationToken);

        return AvailabilityPolicy.EnsureAvailable(carPeriods, reservation.Periods);
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
