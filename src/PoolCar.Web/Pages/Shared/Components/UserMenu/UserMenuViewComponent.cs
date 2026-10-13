using Microsoft.AspNetCore.Mvc;
using Polly;
using PoolCar.Web.Clients;
using PoolCar.Web.Employees;
using PoolCar.Web.Offices;

namespace PoolCar.Web.Pages.Shared.Components.UserMenu;

/// <summary>The signed-in employee with their home office. Falls back to the sign-in name when an API is
/// unavailable, and on the error page, which renders without calling the APIs.</summary>
public sealed class UserMenuViewComponent(
    CurrentEmployee currentEmployee,
    OfficeDirectory officeDirectory,
    ILogger<UserMenuViewComponent> logger) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (Request.Path.StartsWithSegments("/fout", StringComparison.OrdinalIgnoreCase))
        {
            return View(new UserMenuModel(currentEmployee.Name, null, null, [], "/", DetailsLoaded: false));
        }

        var cancellationToken = HttpContext.RequestAborted;
        string? homeOffice = null;
        string? homeOfficeName = null;
        IReadOnlyList<FleetOffice> offices = [];
        var detailsLoaded = false;
        try
        {
            await currentEmployee.LoadAsync(cancellationToken);
            homeOffice = currentEmployee.HomeOffice;
            if (homeOffice is not null)
            {
                offices = await officeDirectory.GetOfficesAsync(cancellationToken);
                homeOfficeName = await officeDirectory.GetNameAsync(homeOffice, cancellationToken);
            }

            detailsLoaded = true;
        }
        catch (Exception exception) when (exception is HttpRequestException or ApiProblemException or TaskCanceledException
            or ExecutionRejectedException)
        {
            logger.LogWarning(exception, "Could not load the user menu details.");
        }

        var returnUrl = (Request.PathBase + Request.Path + Request.QueryString).ToString();
        return View(new UserMenuModel(currentEmployee.Name, homeOffice, homeOfficeName, offices, returnUrl, detailsLoaded));
    }
}

public sealed record UserMenuModel(
    string Name,
    string? HomeOffice,
    string? HomeOfficeName,
    IReadOnlyList<FleetOffice> Offices,
    string ReturnUrl,
    bool DetailsLoaded);
