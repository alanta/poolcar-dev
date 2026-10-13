using System.Net.Http.Json;

namespace PoolCar.Web.Clients;

public class FleetApiClient(HttpClient httpClient)
{
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
}
