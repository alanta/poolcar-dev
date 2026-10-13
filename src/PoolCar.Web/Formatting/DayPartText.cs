using PoolCar.Web.Clients;

namespace PoolCar.Web.Formatting;

/// <summary>Labels and hours of day parts. Hours come from the day part itself, never from a period's
/// instants.</summary>
public static class DayPartText
{
    /// <summary>"Ochtend", "Middag" or "Hele dag".</summary>
    public static string Label(DayPart dayPart) => dayPart switch
    {
        DayPart.Morning => "Ochtend",
        DayPart.Afternoon => "Middag",
        DayPart.FullDay => "Hele dag",
        _ => throw new ArgumentOutOfRangeException(nameof(dayPart), dayPart, null),
    };

    public static TimeOnly Start(DayPart dayPart) => dayPart switch
    {
        DayPart.Morning or DayPart.FullDay => new TimeOnly(8, 0),
        DayPart.Afternoon => new TimeOnly(12, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(dayPart), dayPart, null),
    };

    public static TimeOnly End(DayPart dayPart) => dayPart switch
    {
        DayPart.Morning => new TimeOnly(12, 0),
        DayPart.Afternoon or DayPart.FullDay => new TimeOnly(18, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(dayPart), dayPart, null),
    };

    /// <summary>"08:00–12:00".</summary>
    public static string Hours(DayPart dayPart) => $"{Start(dayPart):HH\\:mm}–{End(dayPart):HH\\:mm}";

    /// <summary>"08–12".</summary>
    public static string ShortHours(DayPart dayPart) => $"{Start(dayPart):HH}–{End(dayPart):HH}";

    /// <summary>The query string value: "ochtend", "middag" or "hele-dag".</summary>
    public static string Slug(DayPart dayPart) => dayPart switch
    {
        DayPart.Morning => "ochtend",
        DayPart.Afternoon => "middag",
        DayPart.FullDay => "hele-dag",
        _ => throw new ArgumentOutOfRangeException(nameof(dayPart), dayPart, null),
    };

    public static bool TryParseSlug(string? slug, out DayPart dayPart)
    {
        foreach (var candidate in Enum.GetValues<DayPart>())
        {
            if (string.Equals(Slug(candidate), slug?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                dayPart = candidate;
                return true;
            }
        }

        dayPart = default;
        return false;
    }
}
