namespace PoolCar.Reservations.Domain;

public static class DayParts
{
    /// <summary>Converts a date and day part to the reserved period.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) ToPeriod(DateOnly date, DayPart part)
    {
        var (from, to) = part switch
        {
            DayPart.Morning => (new TimeOnly(8, 0), new TimeOnly(12, 0)),
            DayPart.Afternoon => (new TimeOnly(12, 0), new TimeOnly(18, 0)),
            DayPart.FullDay => (new TimeOnly(8, 0), new TimeOnly(18, 0)),
            _ => throw new ArgumentOutOfRangeException(nameof(part)),
        };

        return (new DateTimeOffset(date.ToDateTime(from, DateTimeKind.Local)),
                new DateTimeOffset(date.ToDateTime(to, DateTimeKind.Local)));
    }

    /// <summary>Returns the date of an instant.</summary>
    public static DateOnly DateOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToLocalTime().DateTime);

    /// <summary>Returns the day part an instant falls in, or null outside day-part hours.</summary>
    public static DayPart? CurrentDayPart(DateTimeOffset instant)
    {
        var time = TimeOnly.FromDateTime(instant.ToLocalTime().DateTime);

        if (time >= new TimeOnly(8, 0) && time < new TimeOnly(12, 0))
        {
            return DayPart.Morning;
        }

        if (time >= new TimeOnly(12, 0) && time < new TimeOnly(18, 0))
        {
            return DayPart.Afternoon;
        }

        return null;
    }
}
