namespace PoolCar.Web.Clients;

public class FleetClient(HttpClient httpClient)
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
}
