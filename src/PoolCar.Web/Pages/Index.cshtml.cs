using Microsoft.AspNetCore.Mvc.RazorPages;
using PoolCar.Web.Employees;

namespace PoolCar.Web.Pages;

public class IndexModel(CurrentEmployee currentEmployee) : PageModel
{
    public string FirstName { get; private set; } = string.Empty;

    public async Task OnGetAsync()
    {
        await currentEmployee.LoadAsync(HttpContext.RequestAborted);
        FirstName = currentEmployee.FirstName;
    }
}
