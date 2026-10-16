using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PoolCar.Web.Clients;
using PoolCar.Web.ExpiredForms;
using PoolCar.Web.Outages;

namespace PoolCar.Web.Pages;

/// <summary>The error page for unhandled exceptions and for error status codes without a body. It has no
/// handlers, so it renders for every method of the request that failed. It never calls the APIs, so it also renders
/// when they are unavailable. An htmx request that swaps part of the page gets the message as a toast. A form posted
/// from a page that has expired gets a 400 with a link to reload that page.</summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class FoutModel : PageModel
{
    public int Status => RouteData.Values["status"] is string value && int.TryParse(value, out var status)
        ? status
        : StatusCodes.Status500InternalServerError;

    public const string ExpiredMessage = "Deze pagina is verlopen. Laad de pagina opnieuw en probeer het nog eens.";

    public bool IsExpired => Status == StatusCodes.Status400BadRequest && ExpiredFormFilter.IsExpired(HttpContext);

    public string ReloadUrl => ExpiredFormFilter.GetReloadUrl(Request);

    public string TraceId => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    public string Title => IsExpired ? "Pagina verlopen" : Status switch
    {
        404 => "Pagina niet gevonden",
        403 => "Geen toegang",
        503 => "Tijdelijk niet beschikbaar",
        < 500 => "Dat lukte niet",
        _ => "Er ging iets mis",
    };

    public string Message => IsExpired ? ExpiredMessage : Status switch
    {
        404 => "Deze pagina bestaat niet (meer). Controleer het adres, of zoek een auto op kenteken.",
        403 => "Je hebt geen toegang tot deze pagina. Deze pagina is alleen voor medewerkers van de balie.",
        503 => GetUnavailableMessage(ServiceUnavailableMiddleware.GetUnavailableService(HttpContext)),
        < 500 => "Je verzoek kon niet worden verwerkt. Ga terug en probeer het opnieuw.",
        _ => "Sorry, er ging aan onze kant iets mis. Probeer het over een paar minuten opnieuw.",
    };

    public static string GetUnavailableMessage(BackendService? service) => service switch
    {
        BackendService.Reservations => "Reserveringen zijn op dit moment niet beschikbaar. Probeer het later opnieuw.",
        BackendService.Fleet => "Het wagenpark is op dit moment niet beschikbaar. Probeer het later opnieuw.",
        _ => "PoolCar is op dit moment niet beschikbaar. Probeer het later opnieuw.",
    };
}
