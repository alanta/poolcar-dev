using Microsoft.EntityFrameworkCore;
using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Tests.Data;

public class ReservationsDbContextTests(ReservationsDatabase database) : IClassFixture<ReservationsDatabase>
{
    private static readonly DateOnly s_date = new(2026, 11, 17);
    private static readonly DateTimeOffset s_now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task All_migrations_are_applied()
    {
        await using var db = database.CreateContext();

        var pending = await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken);

        pending.ShouldBeEmpty();
        db.Database.GetAppliedMigrations().ShouldBe(db.Database.GetMigrations());
    }

    [Fact]
    public async Task A_reservation_with_periods_round_trips()
    {
        var ct = TestContext.Current.CancellationToken;
        var employee = await database.AddEmployeeAsync();
        var carId = ReservationsDatabase.NewCarId();
        var reservation = Reservation.Create(
            employee.Id, carId, s_date, s_date.AddDays(1), DayPart.Afternoon, ReservationSource.Simulator, s_now).Value!;

        await using (var db = database.CreateContext())
        {
            db.Reservations.Add(reservation);
            await db.SaveChangesAsync(ct);
        }

        await using var read = database.CreateContext();
        var loaded = await read.Reservations.Include(r => r.Periods).SingleAsync(r => r.Id == reservation.Id, ct);

        loaded.EmployeeId.ShouldBe(employee.Id);
        loaded.CarId.ShouldBe(carId);
        loaded.FromDate.ShouldBe(s_date);
        loaded.ToDate.ShouldBe(s_date.AddDays(1));
        loaded.DayPart.ShouldBe(DayPart.Afternoon);
        loaded.Source.ShouldBe(ReservationSource.Simulator);
        loaded.CreatedAt.ShouldBe(s_now);
        loaded.CancelledAt.ShouldBeNull();
        loaded.Periods.Count.ShouldBe(2);
        foreach (var expected in reservation.Periods)
        {
            var period = loaded.Periods.Single(p => p.Id == expected.Id);
            period.Date.ShouldBe(expected.Date);
            period.Start.ShouldBe(expected.Start);
            period.End.ShouldBe(expected.End);
            period.Status.ShouldBe(PeriodStatus.Scheduled);
            period.PickedUpAt.ShouldBeNull();
            period.ReturnedAt.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Periods_store_the_car_of_their_reservation()
    {
        var ct = TestContext.Current.CancellationToken;
        var employee = await database.AddEmployeeAsync();
        var carId = ReservationsDatabase.NewCarId();
        var reservation = Reservation.Create(
            employee.Id, carId, s_date, s_date, DayPart.Morning, ReservationSource.User, s_now).Value!;

        await using (var db = database.CreateContext())
        {
            db.Reservations.Add(reservation);
            await db.SaveChangesAsync(ct);
        }

        await using var read = database.CreateContext();
        var carIds = await read.Database
            .SqlQuery<string>($"SELECT car_id AS \"Value\" FROM reservation_periods WHERE reservation_id = {reservation.Id}")
            .ToListAsync(ct);

        carIds.ShouldBe([carId]);
    }
}
