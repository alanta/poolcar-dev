using Microsoft.EntityFrameworkCore;
using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Data;

/// <summary>Fills an empty database with fictional employees and, optionally, sample reservations.</summary>
public static class DatabaseSeeder
{
    private const int RandomSeed = 20261012;

    public static async Task SeedAsync(
        ReservationsDbContext db,
        TimeProvider time,
        bool includeReservations,
        CancellationToken cancellationToken = default)
    {
        await SeedEmployeesAsync(db, cancellationToken);

        if (includeReservations && !await db.Reservations.AnyAsync(cancellationToken))
        {
            await SeedReservationsAsync(db, time.GetUtcNow(), cancellationToken);
        }
    }

    private static async Task SeedEmployeesAsync(ReservationsDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Employees.AnyAsync(cancellationToken))
        {
            return;
        }

        foreach (var seed in SeedEmployees.All)
        {
            db.Employees.Add(Employee.Create(seed.Id, seed.Name, seed.HomeOffice, EmployeeKind.Seeded).Value!);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedReservationsAsync(
        ReservationsDbContext db,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var today = DayParts.DateOf(now);
        var random = new Random(RandomSeed);
        var employees = SeedEmployees.All;
        var booked = new Dictionary<string, List<ReservationPeriod>>();

        void Add(Guid employeeId, string carId, DateOnly from, DateOnly to, DayPart part, DateTimeOffset? pickUpAt = null)
        {
            var reservation = Reservation.Create(employeeId, carId, from, to, part, now).Value!;
            if (!booked.TryGetValue(carId, out var periods))
            {
                booked[carId] = periods = [];
            }

            if (AvailabilityPolicy.FindConflicts(periods, reservation.Periods).Count > 0)
            {
                return;
            }

            if (pickUpAt is { } pickedUpAt)
            {
                reservation.Periods[0].PickUp(pickedUpAt);
            }

            periods.AddRange(reservation.Periods);
            db.Reservations.Add(reservation);
        }

        Guid PickId() => employees[random.Next(employees.Count)].Id;

        // Utrecht
        var yesterday = today.AddDays(-1);
        Add(PickId(), SeedCars.NotReturned, yesterday, yesterday, DayPart.FullDay, PickUpTime(yesterday));
        Add(PickId(), SeedCars.TomorrowMorning, today.AddDays(1), today.AddDays(1), DayPart.Morning);
        Add(PickId(), SeedCars.TomorrowAndDayAfter, today.AddDays(1), today.AddDays(2), DayPart.FullDay);
        Add(PickId(), SeedCars.PickedUpToday, today, today, DayPart.FullDay, PickUpTime(today));

        var daysAhead = ((7 - (int)today.DayOfWeek) % 7) + 7;
        foreach (var carId in SeedCars.Background)
        {
            var count = random.Next(1, 4);
            for (var i = 0; i < count; i++)
            {
                var length = random.Next(1, 4);
                var from = today.AddDays(random.Next(0, daysAhead + 1 - (length - 1)));
                var part = (DayPart)random.Next(3);
                Add(PickId(), carId, from, from.AddDays(length - 1), part);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static DateTimeOffset PickUpTime(DateOnly date) => DayParts.ToPeriod(date, DayPart.FullDay).Start.AddMinutes(5);
}
