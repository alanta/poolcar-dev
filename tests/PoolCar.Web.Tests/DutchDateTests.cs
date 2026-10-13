using PoolCar.Web.Formatting;
using Shouldly;

namespace PoolCar.Web.Tests;

public class DutchDateTests
{
    [Theory]
    [InlineData("2026-10-13", "di 13 okt")]
    [InlineData("2026-10-18", "zo 18 okt")]
    [InlineData("2026-03-02", "ma 2 mrt")]
    [InlineData("2026-05-01", "vr 1 mei")]
    public void Short_FormatsAbbreviatedDayAndMonth(string date, string expected)
    {
        DutchDate.Short(DateOnly.Parse(date)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("2026-10-13", "dinsdag 13 oktober 2026")]
    [InlineData("2027-01-01", "vrijdag 1 januari 2027")]
    public void Long_FormatsFullDate(string date, string expected)
    {
        DutchDate.Long(DateOnly.Parse(date)).ShouldBe(expected);
    }

    [Fact]
    public void DayAndNumber_FormatsColumnHeading()
    {
        DutchDate.DayAndNumber(new DateOnly(2026, 10, 14)).ShouldBe("wo 14");
    }

    [Theory]
    [InlineData("2026-10-13", "2026-10-13", "di 13 okt")]
    [InlineData("2026-10-13", "2026-10-14", "di 13 en wo 14 oktober")]
    [InlineData("2026-10-31", "2026-11-01", "za 31 okt en zo 1 nov")]
    [InlineData("2026-10-13", "2026-10-16", "di 13 – vr 16 okt")]
    [InlineData("2026-10-29", "2026-11-03", "do 29 okt – di 3 nov")]
    [InlineData("2026-12-31", "2027-01-01", "do 31 dec 2026 en vr 1 jan 2027")]
    [InlineData("2026-12-30", "2027-01-04", "wo 30 dec 2026 – ma 4 jan 2027")]
    public void Range_FormatsPeriod(string from, string to, string expected)
    {
        DutchDate.Range(DateOnly.Parse(from), DateOnly.Parse(to)).ShouldBe(expected);
    }

    [Fact]
    public void Range_WithEndBeforeStart_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DutchDate.Range(new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 13)));
    }

    [Theory]
    [InlineData("di 13 en wo 14 oktober", "Di 13 en wo 14 oktober")]
    [InlineData("", "")]
    public void Capitalize_StartsWithCapital(string text, string expected)
    {
        DutchDate.Capitalize(text).ShouldBe(expected);
    }
}
