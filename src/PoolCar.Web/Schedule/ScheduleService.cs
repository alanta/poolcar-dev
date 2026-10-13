using PoolCar.Web.Clients;

namespace PoolCar.Web.Schedule;

/// <summary>A car with its periods. Periods are placed by their <see cref="ScheduleEntry.Date"/> and
/// <see cref="ScheduleEntry.DayPart"/>; a full-day period fills both the morning and the afternoon.</summary>
public sealed record ScheduleRow(FleetCar Car, IReadOnlyList<ScheduleEntry> Periods)
{
    /// <summary>The periods in a half of a date, by start. Pass <see cref="DayPart.FullDay"/> for the whole
    /// date.</summary>
    public IReadOnlyList<ScheduleEntry> At(DateOnly date, DayPart dayPart) =>
        [.. Periods.Where(p => p.Date == date && Overlaps(p.DayPart, dayPart)).OrderBy(p => p.Start)];

    /// <summary>The period to show in a cell: a scheduled or picked-up period before a returned one; null when the
    /// cell is free.</summary>
    public ScheduleEntry? PrimaryAt(DateOnly date, DayPart dayPart) =>
        At(date, dayPart)
            .OrderBy(p => p.Status == PeriodStatus.Returned ? 1 : 0)
            .ThenByDescending(p => p.Start)
            .FirstOrDefault();

    private static bool Overlaps(DayPart period, DayPart cell) =>
        period == cell || period == DayPart.FullDay || cell == DayPart.FullDay;
}

/// <summary>The dates from <c>from</c> to <c>to</c> and a row per car, in Fleet order.</summary>
public sealed record ScheduleView(IReadOnlyList<DateOnly> Dates, IReadOnlyList<ScheduleRow> Rows);

/// <summary>Combines Fleet cars with the Reservations schedule.</summary>
public class ScheduleService(FleetApiClient fleetApiClient, ReservationsApiClient reservationsApiClient)
{
    /// <summary>The schedule of the cars of an office, optionally of one category, from <paramref name="from"/> to
    /// <paramref name="to"/> (at most 62 days).</summary>
    public async Task<ScheduleView> GetOfficeScheduleAsync(
        string office,
        string? category,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var cars = await fleetApiClient.GetCarsAsync(office, string.IsNullOrEmpty(category) ? null : category, cancellationToken);
        var entries = await reservationsApiClient.GetScheduleAsync([.. cars.Select(c => c.Plate)], from, to, cancellationToken);
        var byCar = entries.ToLookup(e => e.CarId, StringComparer.OrdinalIgnoreCase);

        return new ScheduleView(
            GetDates(from, to),
            [.. cars.Select(car => new ScheduleRow(car, [.. byCar[car.Plate].OrderBy(e => e.Start)]))]);
    }

    /// <summary>The schedule of a single car, or null when the plate is unknown.</summary>
    public async Task<ScheduleRow?> GetCarScheduleAsync(
        string plate,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var car = await fleetApiClient.GetCarAsync(plate, cancellationToken);
        if (car is null)
        {
            return null;
        }

        var entries = await reservationsApiClient.GetScheduleAsync([car.Plate], from, to, cancellationToken);
        return new ScheduleRow(car, [.. entries.OrderBy(e => e.Start)]);
    }

    private static IReadOnlyList<DateOnly> GetDates(DateOnly from, DateOnly to) =>
        [.. Enumerable.Range(0, Math.Max(0, to.DayNumber - from.DayNumber + 1)).Select(from.AddDays)];
}
