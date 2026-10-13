using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PoolCar.Web.Pages;

/// <summary>The error page for unhandled exceptions and for error status codes without a body. It has no
/// handlers, so it renders for every method of the request that failed.</summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class FoutModel : PageModel
{
    public int Status => RouteData.Values["status"] is string value && int.TryParse(value, out var status)
        ? status
        : StatusCodes.Status500InternalServerError;

    public string TraceId => Activity.Current?.Id ?? HttpContext.TraceIdentifier;
}
