using PoolCar.Web.Clients;
using PoolCar.Web.MyReservations;
using Shouldly;

namespace PoolCar.Web.Tests;

public class ReservationOverviewTests
{
    // 11:00 in Amsterdam on Tuesday 13 October 2026
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 13);

    private static readonly FleetCar Elroq = new(
        "KSP73B", "Skoda", "Elroq", "85", "middel", "utrecht", "Groen", 5, 560, 210, new DateOnly(2025, 3, 1), null);

    [Fact]
    public void PickedUpPeriod_IsOnTheRoad_WithReturn()
    {
        var reservation = Reservation(Period(Today, Now.AddHours(-2), Now.AddHours(1), PeriodStatus.PickedUp));

        var overview = Create(reservation);

        var card = overview.OnTheRoad.ShouldHaveSingleItem();
        overview.Planned.ShouldBeEmpty();
        overview.Past.ShouldBeEmpty();
        var period = card.Periods.ShouldHaveSingleItem();
        period.State.ShouldBe(PeriodState.PickedUp);
        period.CanReturn.ShouldBeTrue();
        card.IsOverdue.ShouldBeFalse();
        card.CanCancel.ShouldBeFalse();
    }

    [Fact]
    public void PickedUpPeriodAfterItsEnd_IsOverdue_SinceItsEndTime()
    {
        var reservation = Reservation(
            Period(Today, Now.AddHours(-3), Now.AddHours(-1), PeriodStatus.PickedUp, overdue: true));

        var card = Create(reservation).OnTheRoad.ShouldHaveSingleItem();

        card.IsOverdue.ShouldBeTrue();
        var period = card.Periods.ShouldHaveSingleItem();
        period.State.ShouldBe(PeriodState.Overdue);
        period.CanReturn.ShouldBeTrue();
        period.OverdueSince.ShouldBe("Te laat sinds 10:00");
    }

    [Fact]
    public void OverdueSinceAnEarlierDate_ShowsTheDate()
    {
        var yesterday = Today.AddDays(-1);
        var reservation = Reservation(
            Period(yesterday, Now.AddHours(-26), Now.AddHours(-20), PeriodStatus.PickedUp, overdue: true));

        var period = Create(reservation).OnTheRoad.ShouldHaveSingleItem().Periods.ShouldHaveSingleItem();

        period.OverdueSince.ShouldBe("Te laat sinds ma 12 okt 15:00");
    }

    [Fact]
    public void StartedPeriod_IsPlanned_WithPickUp()
    {
        var reservation = Reservation(Period(Today, Now.AddHours(-1), Now.AddHours(3)));

        var card = Create(reservation).Planned.ShouldHaveSingleItem();

        var period = card.Periods.ShouldHaveSingleItem();
        period.State.ShouldBe(PeriodState.ReadyForPickUp);
        period.CanPickUp.ShouldBeTrue();
        card.CanCancel.ShouldBeTrue();
    }

    [Fact]
    public void FuturePeriod_IsPlanned_WithoutPickUp()
    {
        var reservation = Reservation(Period(Today.AddDays(1), Now.AddHours(21), Now.AddHours(25)));

        var card = Create(reservation).Planned.ShouldHaveSingleItem();

        var period = card.Periods.ShouldHaveSingleItem();
        period.State.ShouldBe(PeriodState.Scheduled);
        period.CanPickUp.ShouldBeFalse();
        card.CanCancel.ShouldBeTrue();
    }

    [Fact]
    public void PeriodStartingNow_CanBePickedUp_AndEndingNow_CannotBe()
    {
        var starting = Reservation(Period(Today, Now, Now.AddHours(1)));
        var ending = Reservation(Period(Today, Now.AddHours(-4), Now));

        Create(starting).Planned.ShouldHaveSingleItem().Periods.ShouldHaveSingleItem().CanPickUp.ShouldBeTrue();
        Create(ending).Planned.ShouldHaveSingleItem().Periods.ShouldHaveSingleItem().CanPickUp.ShouldBeFalse();
    }

    [Fact]
    public void MultiDayReservation_IsOneCard_WithPickUpOnlyForTheStartedPeriod()
    {
        var reservation = Reservation(
            Period(Today, Now.AddHours(-1), Now.AddHours(1)),
            Period(Today.AddDays(1), Now.AddHours(23), Now.AddHours(25))) with
        { To = Today.AddDays(1) };

        var card = Create(reservation).Planned.ShouldHaveSingleItem();

        card.Periods.Select(p => p.CanPickUp).ShouldBe([true, false]);
        card.When.ShouldBe("Di 13 en wo 14 oktober, ochtend");
    }

    [Fact]
    public void NotPickedUpPeriodOfToday_StaysPlanned_Greyed()
    {
        var reservation = Reservation(Period(Today, Now.AddHours(-3), Now.AddHours(-1), notPickedUp: true));

        var overview = Create(reservation);

        var card = overview.Planned.ShouldHaveSingleItem();
        card.Periods.ShouldHaveSingleItem().State.ShouldBe(PeriodState.NotPickedUp);
        card.CanCancel.ShouldBeFalse();
        card.Periods[0].CanPickUp.ShouldBeFalse();
    }

    [Fact]
    public void NotPickedUpPeriodBeforeToday_IsPast()
    {
        var yesterday = Today.AddDays(-1);
        var reservation = Reservation(
            Period(yesterday, Now.AddHours(-25), Now.AddHours(-21), notPickedUp: true)) with
        { From = yesterday, To = yesterday };

        var card = Create(reservation).Past.ShouldHaveSingleItem();

        card.Summary.ShouldBe("Niet opgehaald");
        card.Periods.ShouldBeEmpty();
        card.CanCancel.ShouldBeFalse();
    }

    [Fact]
    public void ReturnedAndCancelledReservations_ArePast_NewestFirst()
    {
        var returned = Reservation(
            Period(Today.AddDays(-2), Now.AddHours(-50), Now.AddHours(-46), PeriodStatus.Returned)) with
        { From = Today.AddDays(-2), To = Today.AddDays(-2) };
        var cancelled = Reservation(Period(Today.AddDays(1), Now.AddHours(21), Now.AddHours(25), PeriodStatus.Cancelled)) with
        { From = Today.AddDays(1), To = Today.AddDays(1) };

        var past = Create(returned, cancelled).Past;

        past.Select(c => c.Summary).ShouldBe(["Geannuleerd", "Ingeleverd"]);
    }

    [Fact]
    public void ReservationsOlderThan30Days_AreLeftOut()
    {
        var old = Today.AddDays(-31);
        var reservation = Reservation(Period(old, Now.AddDays(-31), Now.AddDays(-31).AddHours(4), PeriodStatus.Returned)) with
        { From = old, To = old };

        Create(reservation).Past.ShouldBeEmpty();
    }

    [Fact]
    public void PickedUpPeriodWithLaterPeriods_IsOnTheRoad_AndShowsTheLaterPeriods()
    {
        var reservation = Reservation(
            Period(Today, Now.AddHours(-1), Now.AddHours(1), PeriodStatus.PickedUp),
            Period(Today.AddDays(1), Now.AddHours(23), Now.AddHours(25))) with
        { To = Today.AddDays(1) };

        var overview = Create(reservation);

        overview.Planned.ShouldBeEmpty();
        var card = overview.OnTheRoad.ShouldHaveSingleItem();
        card.Periods.Select(p => p.State).ShouldBe([PeriodState.PickedUp, PeriodState.Scheduled]);
        card.CanCancel.ShouldBeTrue();
    }

    [Fact]
    public void Card_ShowsModelAndFormattedPlate()
    {
        var card = Create(Reservation(Period(Today, Now.AddHours(-1), Now.AddHours(1)))).Planned.ShouldHaveSingleItem();

        card.CarName.ShouldBe("Skoda Elroq");
        card.Plate.ShouldBe("KSP-73-B");
        card.Title.ShouldBe("Di 13 okt, ochtend, Skoda Elroq KSP-73-B");
        card.Periods[0].DateText.ShouldBe("Di 13 okt");
        card.Periods[0].Hours.ShouldBe("08:00–12:00");
    }

    [Fact]
    public void UnknownCar_ShowsOnlyThePlate()
    {
        var reservation = Reservation(Period(Today, Now.AddHours(-1), Now.AddHours(1))) with { CarId = "AB123C" };

        var card = Create(reservation).Planned.ShouldHaveSingleItem();

        card.CarName.ShouldBeNull();
        card.Car.ShouldBe("auto AB-123-C");
    }

    private static ReservationOverview Create(params Reservation[] reservations) =>
        ReservationOverview.Create(reservations, [Elroq], Now, Today);

    private static Reservation Reservation(params ReservationPeriod[] periods) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "KSP73B",
        periods[0].Date,
        periods[0].Date,
        DayPart.Morning,
        ReservationSource.User,
        Now.AddDays(-3),
        null,
        periods.Any(p => p.NotPickedUp),
        periods.Any(p => p.Overdue),
        periods);

    private static ReservationPeriod Period(
        DateOnly date,
        DateTimeOffset start,
        DateTimeOffset end,
        PeriodStatus status = PeriodStatus.Scheduled,
        bool notPickedUp = false,
        bool overdue = false) => new(
            Guid.NewGuid(),
            date,
            start,
            end,
            status,
            status is PeriodStatus.PickedUp or PeriodStatus.Returned ? start : null,
            status == PeriodStatus.Returned ? end : null,
            notPickedUp,
            overdue);
}
