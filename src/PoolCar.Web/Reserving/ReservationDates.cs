using PoolCar.Web.Clients;
using PoolCar.Web.Formatting;

namespace PoolCar.Web.Reserving;

/// <summary>Defaults, validation and Dutch texts for the dates and day part of a new reservation.</summary>
public static class ReservationDates
{
    public const int MaxDays = 14;

    /// <summary>Today, or tomorrow once today's day parts are over.</summary>
    public static DateOnly DefaultFrom(TimeProvider timeProvider)
    {
        var (today, time) = Now(timeProvider);
        return time >= DayPartText.End(DayPart.FullDay) ? today.AddDays(1) : today;
    }

    /// <summary>The morning, or the afternoon when the morning of <paramref name="from"/> is already over.</summary>
    public static DayPart DefaultDayPart(DateOnly from, TimeProvider timeProvider)
    {
        var (today, time) = Now(timeProvider);
        return from == today && time >= DayPartText.End(DayPart.Morning) ? DayPart.Afternoon : DayPart.Morning;
    }

    /// <summary>A Dutch message when the dates can't be booked, otherwise null.</summary>
    public static string? Validate(DateOnly from, DateOnly to, DateOnly today)
    {
        if (from < today)
        {
            return "Je kan niet in het verleden reserveren. Kies vandaag of later.";
        }

        if (to < from)
        {
            return $"De einddatum ligt voor de begindatum. Kies een einddatum vanaf {DutchDate.Short(from)}.";
        }

        if (to.DayNumber - from.DayNumber + 1 > MaxDays)
        {
            return $"Je kan maximaal {MaxDays} dagen achter elkaar reserveren. Kies een kortere periode.";
        }

        return null;
    }

    /// <summary>"Di 13 en wo 14 oktober, ochtend, Utrecht".</summary>
    public static string Summary(DateOnly from, DateOnly to, DayPart dayPart, string officeName) =>
        $"{DutchDate.Capitalize(DutchDate.Range(from, to))}, {DayPartText.Label(dayPart).ToLower(DutchDate.Culture)}, {officeName}";

    /// <summary>"di 13 oktober".</summary>
    public static string DayAndMonth(DateOnly date) => date.ToString("ddd d MMMM", DutchDate.Culture);

    /// <summary>"di 13 oktober", "di 13 oktober en wo 14 oktober" or "di 13 oktober, wo 14 oktober en do 15 oktober".</summary>
    public static string List(IEnumerable<DateOnly> dates)
    {
        var texts = dates.Distinct().Order().Select(DayAndMonth).ToList();
        return texts.Count <= 1
            ? string.Concat(texts)
            : $"{string.Join(", ", texts[..^1])} en {texts[^1]}";
    }

    /// <summary>Whether a period of <paramref name="booked"/> takes (part of) <paramref name="requested"/>.</summary>
    public static bool Overlaps(DayPart booked, DayPart requested) =>
        booked == requested || booked == DayPart.FullDay || requested == DayPart.FullDay;

    private static (DateOnly Today, TimeOnly Time) Now(TimeProvider timeProvider)
    {
        var local = AmsterdamTime.ToLocal(timeProvider.GetUtcNow()).DateTime;
        return (DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local));
    }
}
