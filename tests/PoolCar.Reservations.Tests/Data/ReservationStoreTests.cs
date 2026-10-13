using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PoolCar.Reservations.Data;
using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Tests.Data;

public class ReservationStoreTests(ReservationsDatabase database) : IClassFixture<ReservationsDatabase>
{
    private static readonly DateOnly s_date = new(2026, 11, 19);
    private static readonly DateTimeOffset s_now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    private async Task<Reservation> CreateReservationAsync(string carId, DateOnly from, DateOnly to, DayPart part)
    {
        var employee = await database.AddEmployeeAsync();
        return Reservation.Create(employee.Id, carId, from, to, part, ReservationSource.User, s_now).Value!;
    }

    [Fact]
    public async Task AddAsync_saves_the_reservation()
    {
        var ct = TestContext.Current.CancellationToken;
        var reservation = await CreateReservationAsync(ReservationsDatabase.NewCarId(), s_date, s_date.AddDays(2), DayPart.Morning);

        await using (var db = database.CreateContext())
        {
            var result = await new ReservationStore(db).AddAsync(reservation, ct);
            result.IsSuccess.ShouldBeTrue();
        }

        await using var read = database.CreateContext();
        var periods = await read.ReservationPeriods.CountAsync(p => reservation.Periods.Select(r => r.Id).Contains(p.Id), ct);
        periods.ShouldBe(3);
    }

    [Fact]
    public async Task AddAsync_returns_a_conflict_with_the_reserved_dates()
    {
        var ct = TestContext.Current.CancellationToken;
        var carId = ReservationsDatabase.NewCarId();
        await using (var db = database.CreateContext())
        {
            var existing = await CreateReservationAsync(carId, s_date.AddDays(1), s_date.AddDays(1), DayPart.Afternoon);
            (await new ReservationStore(db).AddAsync(existing, ct)).IsSuccess.ShouldBeTrue();
        }

        await using var context = database.CreateContext();
        var reservation = await CreateReservationAsync(carId, s_date, s_date.AddDays(2), DayPart.FullDay);
        var result = await new ReservationStore(context).AddAsync(reservation, ct);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(DomainError.ConflictCode);
        result.Error.Message.ShouldBe("De auto is al gereserveerd op 20-11-2026.");
    }

    [Fact]
    public async Task AddAsync_maps_a_concurrent_reservation_to_a_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var carId = ReservationsDatabase.NewCarId();
        var concurrent = await CreateReservationAsync(carId, s_date, s_date, DayPart.Morning);
        var interceptor = new BeforeSaveInterceptor(async () =>
        {
            await using var other = database.CreateContext();
            other.Reservations.Add(concurrent);
            await other.SaveChangesAsync(ct);
        });

        await using var db = database.CreateContext(interceptor);
        var reservation = await CreateReservationAsync(carId, s_date, s_date.AddDays(1), DayPart.FullDay);
        var result = await new ReservationStore(db).AddAsync(reservation, ct);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(DomainError.ConflictCode);
        result.Error.Message.ShouldBe("De auto is al gereserveerd op 19-11-2026.");
        reservation.Periods.Count.ShouldBe(2);
        db.ChangeTracker.Entries().ShouldBeEmpty();
        (await db.Reservations.AnyAsync(r => r.Id == reservation.Id, ct)).ShouldBeFalse();
    }

    /// <summary>Runs an action once, just before the context saves its changes.</summary>
    private sealed class BeforeSaveInterceptor(Func<Task> action) : SaveChangesInterceptor
    {
        private Func<Task>? _action = action;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _action, null) is { } run)
            {
                await run();
            }

            return result;
        }
    }
}
