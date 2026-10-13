using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PoolCar.Web.Clients;
using PoolCar.Web.Employees;

namespace PoolCar.Web.Pages;

public class WelkomModel(
    CurrentEmployee currentEmployee,
    FleetApiClient fleetApiClient,
    ReservationsApiClient reservationsApiClient) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Vul je naam in.")]
    [StringLength(200, ErrorMessage = "Je naam is te lang.")]
    public string Name { get; set; } = string.Empty;

    [BindProperty]
    public string? Office { get; set; }

    public string FirstName { get; private set; } = string.Empty;

    public IReadOnlyList<FleetOffice> Offices { get; private set; } = [];

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await currentEmployee.LoadAsync(HttpContext.RequestAborted);
        if (currentEmployee.IsKnown)
        {
            return LocalRedirect(GetReturnUrl());
        }

        Name = currentEmployee.Name;
        FirstName = currentEmployee.FirstName;
        Offices = await fleetApiClient.GetOfficesAsync(HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await currentEmployee.LoadAsync(HttpContext.RequestAborted);
        FirstName = CurrentEmployee.GetFirstName(string.IsNullOrWhiteSpace(Name) ? currentEmployee.Name : Name);
        Offices = await fleetApiClient.GetOfficesAsync(HttpContext.RequestAborted);

        if (!Offices.Any(o => o.Id == Office))
        {
            ModelState.AddModelError(nameof(Office), "Kies je vestiging.");
        }

        if (currentEmployee.Id == Guid.Empty)
        {
            ErrorMessage = "We konden je account niet herkennen. Meld je opnieuw aan.";
            return Page();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await reservationsApiClient.SaveEmployeeAsync(
            currentEmployee.Id,
            Name.Trim(),
            Office!,
            HttpContext.RequestAborted);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Problem.Detail;
            return Page();
        }

        currentEmployee.Set(result.Value);
        return LocalRedirect(GetReturnUrl());
    }

    private string GetReturnUrl() => Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";
}
