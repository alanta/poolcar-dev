using Microsoft.AspNetCore.Mvc;
using PoolCar.Web.Clients;
using PoolCar.Web.Employees;
using PoolCar.Web.Offices;

namespace PoolCar.Web.Pages.Shared.Components.UserMenu;

/// <summary>The signed-in employee with their home office. Falls back to the sign-in name when an API is
/// unavailable, so the layout still renders, for example on the error page.</summary>
public sealed class UserMenuViewComponent(
    CurrentEmployee currentEmployee,
    OfficeDirectory officeDirectory,
    ILogger<UserMenuViewComponent> logger) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        string? homeOffice = null;
        string? homeOfficeName = null;
        IReadOnlyList<FleetOffice> offices = [];
        try
        {
            await currentEmployee.LoadAsync(cancellationToken);
            homeOffice = currentEmployee.HomeOffice;
            if (homeOffice is not null)
            {
                offices = await officeDirectory.GetOfficesAsync(cancellationToken);
                homeOfficeName = await officeDirectory.GetNameAsync(homeOffice, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or ApiProblemException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Could not load the user menu details.");
        }

        var returnUrl = Request.Path.StartsWithSegments("/fout", StringComparison.OrdinalIgnoreCase)
            ? "/"
            : (Request.PathBase + Request.Path + Request.QueryString).ToString();
        return View(new UserMenuModel(currentEmployee.Name, homeOffice, homeOfficeName, offices, returnUrl));
    }
}

public sealed record UserMenuModel(
    string Name,
    string? HomeOffice,
    string? HomeOfficeName,
    IReadOnlyList<FleetOffice> Offices,
    string ReturnUrl);
