using Microsoft.AspNetCore.Mvc.RazorPages;
using PoolCar.Web.Auth;
using PoolCar.Web.Clients;

namespace PoolCar.Web.Pages;

public class IndexModel(FleetClient fleetClient, ReservationsClient reservationsClient) : PageModel
{
    public string UserName { get; private set; } = string.Empty;
    public string ObjectId { get; private set; } = string.Empty;
    public IReadOnlyList<string> Roles { get; private set; } = [];
    public string FleetStatus { get; private set; } = string.Empty;
    public string ReservationsStatus { get; private set; } = string.Empty;

    public async Task OnGetAsync()
    {
        UserName = User.GetName() ?? string.Empty;
        ObjectId = User.GetObjectId() ?? string.Empty;
        Roles = User.GetRoles();

        FleetStatus = await fleetClient.CheckHealthAsync(HttpContext.RequestAborted);
        ReservationsStatus = await reservationsClient.CheckHealthAsync(HttpContext.RequestAborted);
    }
}
