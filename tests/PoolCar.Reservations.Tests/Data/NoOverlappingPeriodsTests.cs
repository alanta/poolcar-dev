using Microsoft.EntityFrameworkCore;
using Npgsql;
using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Tests.Data;

public class NoOverlappingPeriodsTests(ReservationsDatabase database) : IClassFixture<ReservationsDatabase>
{
    private static readonly DateOnly s_date = new(2026, 11, 18);
    private static readonly DateTimeOffset s_now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    private Guid _employeeId;

    private async Task<Reservation> CreateReservationAsync(string carId, DateOnly from, DateOnly to, DayPart part)
    {
        if (_employeeId == Guid.Empty)
        {
            _employeeId = (await database.AddEmployeeAsync()).Id;
        }

        return Reservation.Create(_employeeId, carId, from, to, part, s_now).Value!;
    }

    private async Task SaveAsync(params Reservation[] reservations)
    {
        await using var db = database.CreateContext();
        db.Reservations.AddRange(reservations);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task An_overlapping_active_period_is_refused()
    {
        var carId = ReservationsDatabase.NewCarId();
        await SaveAsync(await CreateReservationAsync(carId, s_date, s_date, DayPart.FullDay));

        var overlapping = await CreateReservationAsync(carId, s_date.AddDays(-1), s_date, DayPart.Afternoon);
        var ex = await Should.ThrowAsync<DbUpdateException>(() => SaveAsync(overlapping));

        ex.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.ExclusionViolation);
    }

    [Fact]
    public async Task Overlapping_periods_of_different_cars_are_allowed()
    {
        await SaveAsync(
            await CreateReservationAsync(ReservationsDatabase.NewCarId(), s_date, s_date, DayPart.FullDay),
            await CreateReservationAsync(ReservationsDatabase.NewCarId(), s_date, s_date, DayPart.FullDay));
    }

    [Fact]
    public async Task Adjacent_periods_are_allowed()
    {
        var carId = ReservationsDatabase.NewCarId();
        var morning = await CreateReservationAsync(carId, s_date, s_date, DayPart.Morning);
        var afternoon = await CreateReservationAsync(carId, s_date, s_date, DayPart.Afternoon);
        morning.Periods[0].End.ShouldBe(afternoon.Periods[0].Start);

        await SaveAsync(morning);
        await SaveAsync(afternoon);
    }

    [Fact]
    public async Task A_cancelled_period_does_not_block_the_car()
    {
        var carId = ReservationsDatabase.NewCarId();
        var cancelled = await CreateReservationAsync(carId, s_date, s_date, DayPart.FullDay);
        cancelled.Cancel(s_now).IsSuccess.ShouldBeTrue();
        await SaveAsync(cancelled);

        await SaveAsync(await CreateReservationAsync(carId, s_date, s_date, DayPart.FullDay));
    }

    [Fact]
    public async Task A_returned_period_does_not_block_the_car()
    {
        var carId = ReservationsDatabase.NewCarId();
        var returned = await CreateReservationAsync(carId, s_date, s_date, DayPart.FullDay);
        var period = returned.Periods[0];
        period.PickUp(period.Start.AddHours(1)).IsSuccess.ShouldBeTrue();
        period.Return(period.Start.AddHours(2)).IsSuccess.ShouldBeTrue();
        await SaveAsync(returned);

        await SaveAsync(await CreateReservationAsync(carId, s_date, s_date, DayPart.FullDay));
    }

    [Fact]
    public async Task Cancelling_a_period_frees_the_car()
    {
        var ct = TestContext.Current.CancellationToken;
        var carId = ReservationsDatabase.NewCarId();
        var reservation = await CreateReservationAsync(carId, s_date, s_date, DayPart.FullDay);
        await SaveAsync(reservation);

        await using (var db = database.CreateContext())
        {
            var stored = await db.Reservations.Include(r => r.Periods).SingleAsync(r => r.Id == reservation.Id, ct);
            stored.Cancel(s_now).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(ct);
        }

        await SaveAsync(await CreateReservationAsync(carId, s_date, s_date, DayPart.Morning));
    }
}
