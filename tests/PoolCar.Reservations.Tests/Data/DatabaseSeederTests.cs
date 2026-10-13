using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using PoolCar.Reservations.Data;
using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Tests.Data;

public class DatabaseSeederTests(PostgresFixture postgres)
{
    private static readonly string[] NeverBooked = ["HPN72V", "GNB11N", "V15PGL"];

    private static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero));

    private static DateOnly Today(FakeTimeProvider time) => DayParts.DateOf(time.GetUtcNow());

    private async Task<ReservationsDatabase> NewDatabaseAsync()
    {
        var database = new ReservationsDatabase(postgres);
        await database.InitializeAsync();
        return database;
    }

    private static async Task<List<(string Car, Reservation Reservation)>> LoadAsync(ReservationsDbContext db, CancellationToken ct)
    {
        var reservations = await db.Reservations.Include(r => r.Periods).ToListAsync(ct);
        return reservations.Select(r => (r.CarId, r)).ToList();
    }

    [Fact]
    public async Task Seeds_ten_employees_per_office_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = await NewDatabaseAsync();
        var time = Clock();

        await using (var db = database.CreateContext())
        {
            await DatabaseSeeder.SeedAsync(db, time, false, ct);
        }

        await using (var db = database.CreateContext())
        {
            await DatabaseSeeder.SeedAsync(db, time, false, ct);
        }

        await using var verify = database.CreateContext();
        var employees = await verify.Employees.ToListAsync(ct);
        employees.Count.ShouldBe(30);
        employees.Select(e => e.Id).Distinct().Count().ShouldBe(30);
        employees.ShouldAllBe(e => e.Kind == EmployeeKind.Seeded);
        employees.GroupBy(e => e.HomeOffice).Select(g => (g.Key, g.Count())).OrderBy(g => g.Key)
            .ShouldBe([("amsterdam", 10), ("eindhoven", 10), ("utrecht", 10)]);
        (await verify.Reservations.CountAsync(ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Seeds_the_fixed_bookings()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = await NewDatabaseAsync();
        var time = Clock();
        var today = Today(time);

        await using (var db = database.CreateContext())
        {
            await DatabaseSeeder.SeedAsync(db, time, true, ct);
        }

        await using var verify = database.CreateContext();
        var all = await LoadAsync(verify, ct);

        var morning = all.Single(r => r.Car == "KSP73B").Reservation;
        morning.FromDate.ShouldBe(today.AddDays(1));
        morning.ToDate.ShouldBe(today.AddDays(1));
        morning.DayPart.ShouldBe(DayPart.Morning);

        var twoDays = all.Single(r => r.Car == "V94FFF").Reservation;
        twoDays.FromDate.ShouldBe(today.AddDays(1));
        twoDays.ToDate.ShouldBe(today.AddDays(2));
        twoDays.DayPart.ShouldBe(DayPart.FullDay);
        twoDays.Periods.Count.ShouldBe(2);

        var pickedUp = all.Single(r => r.Car == "V62PLT").Reservation;
        pickedUp.FromDate.ShouldBe(today);
        pickedUp.DayPart.ShouldBe(DayPart.FullDay);
        var period = pickedUp.Periods.Single();
        period.Status.ShouldBe(PeriodStatus.PickedUp);
        period.PickedUpAt.ShouldBe(DayParts.ToPeriod(today, DayPart.FullDay).Start.AddMinutes(5));

        all.Where(r => NeverBooked.Contains(r.Car)).ShouldBeEmpty();
        all.Select(r => r.Car).Distinct().Count().ShouldBeGreaterThan(30);
    }

    [Fact]
    public async Task Seeds_one_period_that_was_not_returned_in_utrecht()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = await NewDatabaseAsync();
        var time = Clock();

        await using (var db = database.CreateContext())
        {
            await DatabaseSeeder.SeedAsync(db, time, true, ct);
        }

        await using var verify = database.CreateContext();
        var now = time.GetUtcNow();
        var overdue = (await LoadAsync(verify, ct))
            .SelectMany(r => r.Reservation.Periods.Select(p => (r.Car, Period: p)))
            .Where(x => x.Period.IsOverdue(now))
            .ToList();

        overdue.Count.ShouldBe(1);
        overdue[0].Car.ShouldBe("GNK21N");
        overdue[0].Period.Date.ShouldBe(Today(time).AddDays(-1));
    }

    [Fact]
    public async Task Seeding_is_deterministic_for_the_same_day()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = Clock();
        var results = new List<List<(string, DateOnly, DateOnly, DayPart, Guid)>>();

        for (var i = 0; i < 2; i++)
        {
            var database = await NewDatabaseAsync();
            await using (var db = database.CreateContext())
            {
                await DatabaseSeeder.SeedAsync(db, time, true, ct);
            }

            await using var verify = database.CreateContext();
            results.Add((await LoadAsync(verify, ct))
                .Select(r => (r.Car, r.Reservation.FromDate, r.Reservation.ToDate, r.Reservation.DayPart, r.Reservation.EmployeeId))
                .Order()
                .ToList());
        }

        results[1].ShouldBe(results[0]);
    }

    [Fact]
    public async Task Does_not_add_reservations_to_a_database_that_has_some()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = await NewDatabaseAsync();
        var time = Clock();

        await using (var db = database.CreateContext())
        {
            await DatabaseSeeder.SeedAsync(db, time, true, ct);
        }

        int count;
        await using (var db = database.CreateContext())
        {
            count = await db.Reservations.CountAsync(ct);
            await DatabaseSeeder.SeedAsync(db, time, true, ct);
        }

        await using var verify = database.CreateContext();
        (await verify.Reservations.CountAsync(ct)).ShouldBe(count);
    }
}
