using PoolCar.Web.Cars;
using PoolCar.Web.Clients;
using PoolCar.Web.Formatting;
using PoolCar.Web.Schedule;
using Shouldly;

namespace PoolCar.Web.Tests;

public class CarWeekTests
{
    private static readonly Guid Mine = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly HashSet<Guid> MyReservations = [Mine];
    private static readonly DateOnly Tuesday = new(2026, 10, 13);

    [Theory]
    [InlineData("2026-10-12", "2026-10-12")]
    [InlineData("2026-10-13", "2026-10-12")]
    [InlineData("2026-10-18", "2026-10-12")]
    [InlineData("2026-10-19", "2026-10-19")]
    public void StartOfWeek_IsMonday(string date, string monday) =>
        CarWeek.StartOfWeek(DateOnly.Parse(date)).ShouldBe(DateOnly.Parse(monday));

    [Fact]
    public void Create_ShowsMondayToSunday_AndMarksToday()
    {
        var week = CarWeek.Create(Row(), Tuesday, MyReservations);

        week.Select(d => d.Date).ShouldBe([.. Enumerable.Range(12, 7).Select(day => new DateOnly(2026, 10, day))]);
        week.Single(d => d.IsToday).Date.ShouldBe(Tuesday);
        week.ShouldAllBe(d => d.Morning.State == SlotState.Free && d.Afternoon.State == SlotState.Free);
    }

    [Fact]
    public void Create_PlacesPeriodsByDateAndDayPart_AndFillsBothHalvesForAFullDay()
    {
        var row = Row(
            Entry(Other, new DateOnly(2026, 10, 14), DayPart.FullDay),
            Entry(Mine, Tuesday, DayPart.Afternoon),
            Entry(Other, new DateOnly(2026, 10, 12), DayPart.Morning, PeriodStatus.PickedUp, overdue: true),
            Entry(Other, new DateOnly(2026, 10, 12), DayPart.Afternoon, PeriodStatus.Returned),
            Entry(Other, new DateOnly(2026, 10, 15), DayPart.Morning, PeriodStatus.PickedUp));

        var week = CarWeek.Create(row, Tuesday, MyReservations);

        week[0].Morning.State.ShouldBe(SlotState.Overdue);
        week[0].Afternoon.State.ShouldBe(SlotState.Free);
        week[1].Morning.State.ShouldBe(SlotState.Free);
        week[1].Afternoon.State.ShouldBe(SlotState.Mine);
        week[2].Morning.State.ShouldBe(SlotState.Reserved);
        week[2].Afternoon.State.ShouldBe(SlotState.Reserved);
        week[3].Morning.State.ShouldBe(SlotState.PickedUp);
        week[3].Afternoon.State.ShouldBe(SlotState.Free);
    }

    [Fact]
    public void Slot_HasTextLabel()
    {
        new WeekSlot(DayPart.Morning, SlotState.Free).Label.ShouldBe("Vrij");
        new WeekSlot(DayPart.Morning, SlotState.Reserved).Label.ShouldBe("Gereserveerd");
        new WeekSlot(DayPart.Morning, SlotState.PickedUp).Label.ShouldBe("Opgehaald");
        new WeekSlot(DayPart.Morning, SlotState.Overdue).Label.ShouldBe("Te laat");
        new WeekSlot(DayPart.Morning, SlotState.Mine).Label.ShouldBe("Jouw reservering");
    }

    [Fact]
    public void Upcoming_SkipsFinishedAndReturnedPeriods_ButKeepsOverdueCars()
    {
        var now = new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
        var overdue = Entry(Other, new DateOnly(2026, 10, 12), DayPart.Morning, PeriodStatus.PickedUp, overdue: true);
        var running = Entry(Mine, Tuesday, DayPart.Morning, PeriodStatus.PickedUp);
        var later = Entry(Other, new DateOnly(2026, 10, 20), DayPart.FullDay);
        var row = Row(
            later,
            Entry(Other, new DateOnly(2026, 10, 12), DayPart.Afternoon, PeriodStatus.Returned),
            Entry(Other, new DateOnly(2026, 10, 9), DayPart.Morning),
            running,
            overdue);

        var upcoming = CarWeek.Upcoming(row, now, MyReservations);

        upcoming.Select(b => b.Period).ShouldBe([overdue, running, later]);
        upcoming.Select(b => b.IsMine).ShouldBe([false, true, false]);
        upcoming.Select(b => b.StatusLabel).ShouldBe(["Te laat", "Opgehaald", "Gereserveerd"]);
    }

    private static ScheduleRow Row(params ScheduleEntry[] periods) => new(
        new FleetCar("HPN72V", "Kia", "Niro", "EV", "middel", "utrecht", "Grijs", 5, 460, 150, new DateOnly(2024, 3, 1), null),
        periods);

    private static ScheduleEntry Entry(
        Guid reservationId,
        DateOnly date,
        DayPart dayPart,
        PeriodStatus status = PeriodStatus.Scheduled,
        bool overdue = false)
    {
        // Instants in UTC during summer time, as the API stores them
        var start = new DateTimeOffset(date.ToDateTime(DayPartText.Start(dayPart).AddHours(-2)), TimeSpan.Zero);
        var end = new DateTimeOffset(date.ToDateTime(DayPartText.End(dayPart).AddHours(-2)), TimeSpan.Zero);
        return new ScheduleEntry(
            reservationId, Guid.NewGuid(), "HPN72V", date, dayPart, start, end, status, "Anna", false, overdue, false);
    }
}
