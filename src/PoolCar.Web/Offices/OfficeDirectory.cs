using Microsoft.Extensions.Caching.Memory;
using PoolCar.Web.Clients;

namespace PoolCar.Web.Offices;

/// <summary>The Fleet offices, cached because the fleet is a fixed snapshot.</summary>
public sealed class OfficeDirectory(FleetApiClient fleetApiClient, IMemoryCache cache)
{
    private const string CacheKey = "PoolCar.Offices";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    public async Task<IReadOnlyList<FleetOffice>> GetOfficesAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<FleetOffice>? offices) && offices is not null)
        {
            return offices;
        }

        offices = await fleetApiClient.GetOfficesAsync(cancellationToken);
        if (offices.Count > 0)
        {
            cache.Set(CacheKey, offices, CacheDuration);
        }

        return offices;
    }

    /// <summary>The name of the office, or the id when the office is unknown.</summary>
    public async Task<string?> GetNameAsync(string? officeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(officeId))
        {
            return null;
        }

        var offices = await GetOfficesAsync(cancellationToken);
        return offices.FirstOrDefault(o => string.Equals(o.Id, officeId, StringComparison.OrdinalIgnoreCase))?.Name
            ?? officeId;
    }
}
