using System.Net;
using System.Net.Http.Json;

namespace PoolCar.Web.Clients;

public class FleetApiClient(HttpClient httpClient)
{
    /// <summary>The maximum number of plates per request; larger lists are split.</summary>
    public const int MaxPlatesPerRequest = 200;

    public async Task<string> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync("/health", cancellationToken);
            return response.IsSuccessStatusCode ? "ok" : "error";
        }
        catch (HttpRequestException)
        {
            return "error";
        }
    }

    public async Task<IReadOnlyList<FleetOffice>> GetOfficesAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<List<FleetOffice>>("/offices", cancellationToken) ?? [];

    public async Task<IReadOnlyList<FleetCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<List<FleetCategory>>("/categories", cancellationToken) ?? [];

    public async Task<IReadOnlyList<FleetCar>> GetCarsAsync(
        string? office = null,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(office))
        {
            query.Add($"office={Uri.EscapeDataString(office)}");
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query.Add($"category={Uri.EscapeDataString(category)}");
        }

        var uri = query.Count == 0 ? "/cars" : $"/cars?{string.Join('&', query)}";
        return await httpClient.GetFromJsonAsync<List<FleetCar>>(uri, cancellationToken) ?? [];
    }

    /// <summary>The car with the plate, or null when the plate is unknown.</summary>
    public async Task<FleetCar?> GetCarAsync(string plate, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"/cars/{Uri.EscapeDataString(plate)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FleetCar>(cancellationToken);
    }

    /// <summary>The cars with the given plates; unknown plates are left out.</summary>
    public async Task<IReadOnlyList<FleetCar>> GetCarsByPlatesAsync(
        IReadOnlyCollection<string> plates,
        CancellationToken cancellationToken = default)
    {
        var cars = new List<FleetCar>();
        foreach (var batch in plates.Distinct().Chunk(MaxPlatesPerRequest))
        {
            var uri = $"/cars?plates={string.Join(',', batch.Select(Uri.EscapeDataString))}";
            cars.AddRange(await httpClient.GetFromJsonAsync<List<FleetCar>>(uri, cancellationToken) ?? []);
        }

        return cars;
    }

    /// <summary>Up to 10 cars whose plate contains <paramref name="query"/>.</summary>
    public async Task<IReadOnlyList<FleetCar>> SearchCarsAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var uri = $"/cars/search?q={Uri.EscapeDataString(query)}";
        return await httpClient.GetFromJsonAsync<List<FleetCar>>(uri, cancellationToken) ?? [];
    }
}
