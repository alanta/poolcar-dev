using System.Globalization;

namespace PoolCar.Web.Formatting;

/// <summary>Formats dates in Dutch (<c>nl-NL</c>), such as "di 13 okt" and "dinsdag 13 oktober 2026".</summary>
public static class DutchDate
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>"di 13 okt".</summary>
    public static string Short(DateOnly date) => date.ToString("ddd d MMM", Culture);

    /// <summary>"dinsdag 13 oktober 2026".</summary>
    public static string Long(DateOnly date) => date.ToString("dddd d MMMM yyyy", Culture);

    /// <summary>"di 13", for example for a column heading.</summary>
    public static string DayAndNumber(DateOnly date) => date.ToString("ddd d", Culture);

    /// <summary>A period of dates: "di 13 okt", "di 13 en wo 14 oktober", "di 13 – vr 16 okt" or
    /// "do 29 okt – di 3 nov". The year is added when the dates are in different years.</summary>
    public static string Range(DateOnly from, DateOnly to)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(from, to);

        if (from == to)
        {
            return Short(from);
        }

        var sameMonth = from.Year == to.Year && from.Month == to.Month;
        var nextDay = to.DayNumber == from.DayNumber + 1;
        if (from.Year != to.Year)
        {
            var separator = nextDay ? " en " : " – ";
            return $"{Short(from)} {from.Year}{separator}{Short(to)} {to.Year}";
        }

        return (nextDay, sameMonth) switch
        {
            (true, true) => $"{DayAndNumber(from)} en {DayAndNumber(to)} {to.ToString("MMMM", Culture)}",
            (true, false) => $"{Short(from)} en {Short(to)}",
            (false, true) => $"{DayAndNumber(from)} – {Short(to)}",
            (false, false) => $"{Short(from)} – {Short(to)}",
        };
    }

    /// <summary>Starts the text with a capital, for a date at the start of a sentence: "Di 13 okt".</summary>
    public static string Capitalize(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpper(text[0], Culture) + text[1..];
}
