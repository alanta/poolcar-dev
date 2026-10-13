using PoolCar.Web.Clients;
using PoolCar.Web.Home;
using Shouldly;

namespace PoolCar.Web.Tests;

public class NextReservationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Find_WithoutReservations_ReturnsNull()
    {
        NextReservation.Find([], Now).ShouldBeNull();
    }

    [Fact]
    public void Find_ReturnsEarliestScheduledPeriodThatHasNotEnded()
    {
        var ended = Period(PeriodStatus.Scheduled, Now.AddHours(-5), Now.AddHours(-1));
        var later = Period(PeriodStatus.Scheduled, Now.AddDays(2), Now.AddDays(2).AddHours(4));
        var sooner = Period(PeriodStatus.Scheduled, Now.AddDays(1), Now.AddDays(1).AddHours(4));

        var next = NextReservation.Find([Reservation("AA11BB", ended, later), Reservation("CC22DD", sooner)], Now);

        next.ShouldNotBeNull();
        next.Reservation.CarId.ShouldBe("CC22DD");
        next.Period.ShouldBe(sooner);
    }

    [Fact]
    public void Find_IncludesScheduledPeriodThatIsRunningNow()
    {
        var running = Period(PeriodStatus.Scheduled, Now.AddHours(-1), Now.AddHours(3));

        NextReservation.Find([Reservation("AA11BB", running)], Now)!.Period.ShouldBe(running);
    }

    [Fact]
    public void Find_IncludesOverduePickedUpPeriod()
    {
        var overdue = Period(PeriodStatus.PickedUp, Now.AddHours(-6), Now.AddHours(-2), overdue: true);
        var upcoming = Period(PeriodStatus.Scheduled, Now.AddDays(1), Now.AddDays(1).AddHours(4));

        NextReservation.Find([Reservation("AA11BB", upcoming), Reservation("CC22DD", overdue)], Now)!
            .Period.ShouldBe(overdue);
    }

    [Theory]
    [InlineData(PeriodStatus.Returned)]
    [InlineData(PeriodStatus.Cancelled)]
    public void Find_SkipsReturnedAndCancelledPeriods(PeriodStatus status)
    {
        var period = Period(status, Now.AddHours(1), Now.AddHours(5));

        NextReservation.Find([Reservation("AA11BB", period)], Now).ShouldBeNull();
    }

    private static Reservation Reservation(string carId, params ReservationPeriod[] periods) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        carId,
        DateOnly.FromDateTime(periods[0].Start.Date),
        DateOnly.FromDateTime(periods[^1].Start.Date),
        DayPart.Morning,
        Now.AddDays(-1),
        null,
        false,
        false,
        periods);

    private static ReservationPeriod Period(
        PeriodStatus status,
        DateTimeOffset start,
        DateTimeOffset end,
        bool overdue = false) => new(
        Guid.NewGuid(),
        DateOnly.FromDateTime(start.Date),
        start,
        end,
        status,
        status == PeriodStatus.PickedUp ? start : null,
        null,
        false,
        overdue);
}
