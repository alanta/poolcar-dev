using System.Globalization;

namespace PoolCar.Web.Formatting;

/// <summary>Shows instants in the Europe/Amsterdam time zone, whatever the server's time zone.</summary>
public static class AmsterdamTime
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    public static DateTimeOffset ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone);

    /// <summary>"14:05".</summary>
    public static string Time(DateTimeOffset instant) => ToLocal(instant).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The current date in Amsterdam.</summary>
    public static DateOnly Today(TimeProvider timeProvider) => DateOnly.FromDateTime(ToLocal(timeProvider.GetUtcNow()).DateTime);
}
