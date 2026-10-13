using PoolCar.Web.Clients;
using PoolCar.Web.Reserving;
using Shouldly;

namespace PoolCar.Web.Tests;

public class ReservationDatesTests
{
    private static readonly DateOnly Today = new(2026, 10, 13);

    [Fact]
    public void Validate_AcceptsFourteenDays()
    {
        ReservationDates.Validate(Today, Today.AddDays(13), Today).ShouldBeNull();
    }

    [Fact]
    public void Validate_RejectsFifteenDays_EndBeforeStart_AndThePast()
    {
        ReservationDates.Validate(Today, Today.AddDays(14), Today).ShouldNotBeNull().ShouldContain("maximaal 14 dagen");
        ReservationDates.Validate(Today.AddDays(1), Today, Today).ShouldNotBeNull().ShouldContain("einddatum ligt voor de begindatum");
        ReservationDates.Validate(Today.AddDays(-1), Today, Today).ShouldNotBeNull().ShouldContain("verleden");
    }

    [Fact]
    public void Summary_CombinesDatesDayPartAndOffice()
    {
        ReservationDates.Summary(Today, Today.AddDays(1), DayPart.Morning, "Utrecht")
            .ShouldBe("Di 13 en wo 14 oktober, ochtend, Utrecht");
        ReservationDates.Summary(Today, Today, DayPart.FullDay, "Amsterdam")
            .ShouldBe("Di 13 okt, hele dag, Amsterdam");
    }

    [Fact]
    public void List_NamesEveryDate()
    {
        ReservationDates.List([Today]).ShouldBe("di 13 oktober");
        ReservationDates.List([Today.AddDays(1), Today]).ShouldBe("di 13 oktober en wo 14 oktober");
        ReservationDates.List([Today, Today.AddDays(1), Today.AddDays(2)])
            .ShouldBe("di 13 oktober, wo 14 oktober en do 15 oktober");
    }

    [Theory]
    [InlineData(DayPart.Morning, DayPart.Morning, true)]
    [InlineData(DayPart.Morning, DayPart.Afternoon, false)]
    [InlineData(DayPart.FullDay, DayPart.Afternoon, true)]
    [InlineData(DayPart.Afternoon, DayPart.FullDay, true)]
    public void Overlaps_ComparesDayParts(DayPart booked, DayPart requested, bool expected)
    {
        ReservationDates.Overlaps(booked, requested).ShouldBe(expected);
    }
}
