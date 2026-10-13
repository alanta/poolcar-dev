using System.Globalization;

namespace PoolCar.EndToEnd.Tests;

/// <summary>Dates as the web app shows them: in the Europe/Amsterdam time zone and in Dutch.</summary>
public static class DutchCalendar
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("nl-NL");
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    public static DateTimeOffset Now => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Amsterdam);

    public static DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    /// <summary>The value of a date field: "2026-10-13".</summary>
    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>"Di 13 okt".</summary>
    public static string Short(DateOnly date) => Capitalize(ShortLower(date));

    /// <summary>"di 13 okt".</summary>
    public static string ShortLower(DateOnly date) => date.ToString("ddd d MMM", Culture);

    /// <summary>"di 13 oktober".</summary>
    public static string DayAndMonth(DateOnly date) => date.ToString("ddd d MMMM", Culture);

    /// <summary>"Dinsdag 13 oktober 2026".</summary>
    public static string Long(DateOnly date) => Capitalize(date.ToString("dddd d MMMM yyyy", Culture));

    public static int Week(DateOnly date) => ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue));

    public static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static string Capitalize(string text) => char.ToUpper(text[0], Culture) + text[1..];
}
