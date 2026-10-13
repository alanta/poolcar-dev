using PoolCar.Web.Clients;
using PoolCar.Web.Formatting;
using Shouldly;

namespace PoolCar.Web.Tests;

public class DayPartTextTests
{
    [Theory]
    [InlineData(DayPart.Morning, "Ochtend", "08:00–12:00", "08–12", "ochtend")]
    [InlineData(DayPart.Afternoon, "Middag", "12:00–18:00", "12–18", "middag")]
    [InlineData(DayPart.FullDay, "Hele dag", "08:00–18:00", "08–18", "hele-dag")]
    public void FormatsDayPart(DayPart dayPart, string label, string hours, string shortHours, string slug)
    {
        DayPartText.Label(dayPart).ShouldBe(label);
        DayPartText.Hours(dayPart).ShouldBe(hours);
        DayPartText.ShortHours(dayPart).ShouldBe(shortHours);
        DayPartText.Slug(dayPart).ShouldBe(slug);
    }

    [Theory]
    [InlineData(DayPart.Morning, 8, 12)]
    [InlineData(DayPart.Afternoon, 12, 18)]
    [InlineData(DayPart.FullDay, 8, 18)]
    public void StartAndEnd_ComeFromDayPart(DayPart dayPart, int start, int end)
    {
        DayPartText.Start(dayPart).ShouldBe(new TimeOnly(start, 0));
        DayPartText.End(dayPart).ShouldBe(new TimeOnly(end, 0));
    }

    [Theory]
    [InlineData("ochtend", true, DayPart.Morning)]
    [InlineData("Middag", true, DayPart.Afternoon)]
    [InlineData(" hele-dag ", true, DayPart.FullDay)]
    [InlineData("avond", false, DayPart.Morning)]
    [InlineData(null, false, DayPart.Morning)]
    public void TryParseSlug_ReadsQueryValue(string? slug, bool parsed, DayPart expected)
    {
        DayPartText.TryParseSlug(slug, out var dayPart).ShouldBe(parsed);
        dayPart.ShouldBe(expected);
    }
}
