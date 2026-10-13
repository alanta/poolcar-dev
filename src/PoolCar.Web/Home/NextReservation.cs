using PoolCar.Web.Clients;

namespace PoolCar.Web.Home;

/// <summary>A period of a reservation, with the reservation it belongs to.</summary>
public sealed record UpcomingPeriod(Reservation Reservation, ReservationPeriod Period);

public static class NextReservation
{
    /// <summary>The first period that is picked up or scheduled and has not ended yet. A picked-up period counts
    /// until the car is returned, also when it is overdue.</summary>
    public static UpcomingPeriod? Find(IEnumerable<Reservation> reservations, DateTimeOffset now) =>
        reservations
            .SelectMany(reservation => reservation.Periods.Select(period => new UpcomingPeriod(reservation, period)))
            .Where(upcoming => upcoming.Period.Status switch
            {
                PeriodStatus.PickedUp => true,
                PeriodStatus.Scheduled => upcoming.Period.End > now,
                _ => false,
            })
            .OrderBy(upcoming => upcoming.Period.Start)
            .ThenBy(upcoming => upcoming.Period.Id)
            .FirstOrDefault();
}
