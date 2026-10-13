using PoolCar.Web.Clients;
using PoolCar.Web.Formatting;

namespace PoolCar.Web.Availability;

/// <summary>The current day part for display.</summary>
public static class CurrentDayPart
{
    /// <summary>The current morning or afternoon, or null outside 08:00–18:00.</summary>
    public static DayPart? At(DateTimeOffset instant)
    {
        var time = TimeOnly.FromDateTime(AmsterdamTime.ToLocal(instant).DateTime);
        foreach (var part in (DayPart[])[DayPart.Morning, DayPart.Afternoon])
        {
            if (time >= DayPartText.Start(part) && time < DayPartText.End(part))
            {
                return part;
            }
        }

        return null;
    }

    public static DayPart? Now(TimeProvider timeProvider) => At(timeProvider.GetUtcNow());
}
