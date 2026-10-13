namespace PoolCar.Reservations.Tests.Api;

/// <summary>Instants that fall in a known day part on a date.</summary>
internal static class TestTimes
{
    public static readonly DateOnly Today = new(2026, 11, 10);

    public static DateTimeOffset Morning(DateOnly date) => At(date, 9);

    public static DateTimeOffset Afternoon(DateOnly date) => At(date, 13);

    public static DateTimeOffset OutsideHours(DateOnly date) => At(date, 20);

    private static DateTimeOffset At(DateOnly date, int hour) =>
        new(date.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);
}
