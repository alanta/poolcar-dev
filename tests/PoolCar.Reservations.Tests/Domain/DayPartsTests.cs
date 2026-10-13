using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Tests.Domain;

public class DayPartsTests
{
    private static readonly DateOnly s_date = new(2026, 10, 14);

    [Fact]
    public void Morning_period_lasts_four_hours()
    {
        var (start, end) = DayParts.ToPeriod(s_date, DayPart.Morning);

        (end - start).ShouldBe(TimeSpan.FromHours(4));
    }

    [Fact]
    public void Afternoon_period_lasts_six_hours()
    {
        var (start, end) = DayParts.ToPeriod(s_date, DayPart.Afternoon);

        (end - start).ShouldBe(TimeSpan.FromHours(6));
    }

    [Fact]
    public void Full_day_period_lasts_ten_hours()
    {
        var (start, end) = DayParts.ToPeriod(s_date, DayPart.FullDay);

        (end - start).ShouldBe(TimeSpan.FromHours(10));
    }

    [Fact]
    public void Morning_ends_when_afternoon_starts()
    {
        var morning = DayParts.ToPeriod(s_date, DayPart.Morning);
        var afternoon = DayParts.ToPeriod(s_date, DayPart.Afternoon);

        morning.End.ShouldBe(afternoon.Start);
    }

    [Fact]
    public void Full_day_spans_morning_and_afternoon()
    {
        var fullDay = DayParts.ToPeriod(s_date, DayPart.FullDay);

        fullDay.Start.ShouldBe(DayParts.ToPeriod(s_date, DayPart.Morning).Start);
        fullDay.End.ShouldBe(DayParts.ToPeriod(s_date, DayPart.Afternoon).End);
    }

    [Fact]
    public void Next_date_starts_after_the_previous_date_ends()
    {
        var today = DayParts.ToPeriod(s_date, DayPart.FullDay);
        var tomorrow = DayParts.ToPeriod(s_date.AddDays(1), DayPart.FullDay);

        tomorrow.Start.ShouldBeGreaterThan(today.End);
    }

    [Theory]
    [InlineData(DayPart.Morning)]
    [InlineData(DayPart.Afternoon)]
    [InlineData(DayPart.FullDay)]
    public void Period_falls_on_the_requested_date(DayPart part)
    {
        var (start, end) = DayParts.ToPeriod(s_date, part);

        DayParts.DateOf(start).ShouldBe(s_date);
        DayParts.DateOf(end).ShouldBe(s_date);
    }

    [Fact]
    public void Unknown_day_part_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DayParts.ToPeriod(s_date, (DayPart)42));
    }

    [Theory]
    [InlineData(DayPart.Morning)]
    [InlineData(DayPart.Afternoon)]
    public void Instant_inside_a_day_part_belongs_to_that_day_part(DayPart part)
    {
        var (start, end) = DayParts.ToPeriod(s_date, part);

        DayParts.CurrentDayPart(start).ShouldBe(part);
        DayParts.CurrentDayPart(end.AddTicks(-1)).ShouldBe(part);
    }

    [Fact]
    public void Instant_outside_day_part_hours_has_no_day_part()
    {
        var (start, end) = DayParts.ToPeriod(s_date, DayPart.FullDay);

        DayParts.CurrentDayPart(start.AddTicks(-1)).ShouldBeNull();
        DayParts.CurrentDayPart(end).ShouldBeNull();
    }

    [Fact]
    public void Morning_instant_has_morning_day_part()
    {
        DayParts.CurrentDayPart(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero)).ShouldBe(DayPart.Morning);
    }

    [Fact]
    public void Afternoon_instant_has_afternoon_day_part()
    {
        DayParts.CurrentDayPart(new DateTimeOffset(2026, 10, 14, 13, 0, 0, TimeSpan.Zero))
            .ShouldBe(DayPart.Afternoon);
    }

    [Fact]
    public void Evening_instant_has_no_day_part()
    {
        DayParts.CurrentDayPart(new DateTimeOffset(2026, 10, 14, 20, 0, 0, TimeSpan.Zero)).ShouldBeNull();
    }
}
