using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PoolCar.Web.Clients;
using PoolCar.Web.Employees;
using PoolCar.Web.Htmx;
using PoolCar.Web.Offices;

namespace PoolCar.Web.Pages;

public class ThuislocatieModel(
    CurrentEmployee currentEmployee,
    OfficeDirectory officeDirectory,
    ReservationsApiClient reservationsApiClient) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public string? Office { get; set; }

    public string? HomeOffice { get; private set; }

    public IReadOnlyList<FleetOffice> Offices { get; private set; } = [];

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        var office = Offices.FirstOrDefault(o => o.Id == Office);
        if (office is null)
        {
            ErrorMessage = "Kies een vestiging uit de lijst.";
            return Page();
        }

        if (currentEmployee.Employee is not { } employee)
        {
            return LocalRedirect("/welkom");
        }

        var result = await reservationsApiClient.ChangeHomeOfficeAsync(employee, office.Id, HttpContext.RequestAborted);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Problem.Detail;
            return Page();
        }

        currentEmployee.Set(result.Value);
        Toast.Success($"Je thuislocatie is {office.Name}.").Store(TempData);
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/");
    }

    private async Task LoadAsync()
    {
        await currentEmployee.LoadAsync(HttpContext.RequestAborted);
        HomeOffice = currentEmployee.HomeOffice;
        Offices = await officeDirectory.GetOfficesAsync(HttpContext.RequestAborted);
    }
}
