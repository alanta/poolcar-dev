using PoolCar.Web.Clients;

namespace PoolCar.Web.Availability;

/// <summary>A booking request: an office, an optional category, and a day part on every date from
/// <see cref="From"/> to <see cref="To"/>.</summary>
public sealed record AvailabilityRequest(
    string Office,
    DateOnly From,
    DateOnly To,
    DayPart DayPart,
    string? Category = null);

/// <summary>The number of free cars of a category at the office, out of all cars of that category there.</summary>
public sealed record CategoryAvailability(FleetCategory Category, int Available, int Total)
{
    public bool HasAvailable => Available > 0;
}

/// <summary>Counts for every category (in display order, including categories without free cars) and the free
/// cars, limited to the requested category when there is one, sorted by category and then range.</summary>
public sealed record AvailabilityResult(IReadOnlyList<CategoryAvailability> Categories, IReadOnlyList<FleetCar> Cars);

/// <summary>Combines the Fleet cars of an office with the cars that Reservations reports as busy.</summary>
public class AvailabilityService(FleetApiClient fleetApiClient, ReservationsApiClient reservationsApiClient)
{
    /// <summary>The cars that are free for the request. Fails with the Reservations problem when the request breaks
    /// a booking rule, such as more than 14 days.</summary>
    public async Task<ApiResult<AvailabilityResult>> GetAvailableAsync(
        AvailabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        var (categories, cars) = await GetOfficeCarsAsync(request.Office, cancellationToken);
        var busy = await reservationsApiClient.GetBusyCarIdsAsync(
            [.. cars.Select(c => c.Plate)],
            request.From,
            request.To,
            request.DayPart,
            cancellationToken);

        return busy.Map(ids => Compose(categories, cars, ids, request.Category));
    }

    /// <summary>The cars that are free now, until the end of the current day part.</summary>
    public async Task<AvailabilityResult> GetAvailableNowAsync(
        string office,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        var (categories, cars) = await GetOfficeCarsAsync(office, cancellationToken);
        var inUse = await reservationsApiClient.GetCarIdsInUseAsync([.. cars.Select(c => c.Plate)], cancellationToken);

        return Compose(categories, cars, inUse, category);
    }

    private async Task<(IReadOnlyList<FleetCategory> Categories, IReadOnlyList<FleetCar> Cars)> GetOfficeCarsAsync(
        string office,
        CancellationToken cancellationToken)
    {
        var categories = fleetApiClient.GetCategoriesAsync(cancellationToken);
        var cars = fleetApiClient.GetCarsAsync(office: office, cancellationToken: cancellationToken);
        return (await categories, await cars);
    }

    private static AvailabilityResult Compose(
        IReadOnlyList<FleetCategory> categories,
        IReadOnlyList<FleetCar> cars,
        IReadOnlyCollection<string> unavailableIds,
        string? category)
    {
        var unavailable = unavailableIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var free = cars.Where(c => !unavailable.Contains(c.Plate)).ToList();

        var counts = categories
            .OrderBy(c => c.SortOrder)
            .Select(c => new CategoryAvailability(
                c,
                free.Count(car => car.Category == c.Id),
                cars.Count(car => car.Category == c.Id)))
            .ToList();

        var selected = string.IsNullOrEmpty(category) ? free : free.Where(c => c.Category == category).ToList();
        return new AvailabilityResult(counts, selected);
    }
}
