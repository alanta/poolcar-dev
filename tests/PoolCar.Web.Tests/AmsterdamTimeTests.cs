using Microsoft.Extensions.Time.Testing;
using PoolCar.Web.Formatting;
using Shouldly;

namespace PoolCar.Web.Tests;

public class AmsterdamTimeTests
{
    [Theory]
    [InlineData("2026-10-13T06:00:00+00:00", "08:00")]
    [InlineData("2026-10-13T10:00:00+00:00", "12:00")]
    [InlineData("2026-12-01T07:00:00+00:00", "08:00")]
    [InlineData("2026-12-01T16:59:00+00:00", "17:59")]
    [InlineData("2026-10-13T08:00:00+02:00", "08:00")]
    [InlineData("2026-10-25T00:30:00+00:00", "02:30")]
    [InlineData("2026-10-25T01:30:00+00:00", "02:30")]
    [InlineData("2026-03-29T01:00:00+00:00", "03:00")]
    [InlineData("2026-10-13T22:30:00+00:00", "00:30")]
    public void Time_FormatsInstantInAmsterdam(string instant, string expected)
    {
        AmsterdamTime.Time(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("2026-10-13T21:59:00+00:00", "2026-10-13")]
    [InlineData("2026-10-13T22:00:00+00:00", "2026-10-14")]
    [InlineData("2026-12-31T23:30:00+00:00", "2027-01-01")]
    public void Today_IsTheDateInAmsterdam(string now, string expected)
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture));

        AmsterdamTime.Today(time).ShouldBe(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }
}
