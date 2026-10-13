using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PoolCar.Web.Htmx;

public static class PageModelHtmxExtensions
{
    /// <summary>The partial for an htmx request that swaps part of the page, the full page otherwise.</summary>
    public static IActionResult PartialOrPage(this PageModel page, string partialName, object? model = null) =>
        page.Request.IsHtmxPartial() ? page.Partial(partialName, model) : page.Page();

    /// <summary>The partial with the given status code, for example 409 or 422 with a message.</summary>
    public static PartialViewResult Partial(this PageModel page, string partialName, object? model, int statusCode)
    {
        var result = page.Partial(partialName, model);
        result.StatusCode = statusCode;
        return result;
    }

    /// <summary>Only a toast, for an htmx request with <c>hx-swap="none"</c>.</summary>
    public static PartialViewResult ToastResult(this PageModel page, Toast toast, int statusCode = StatusCodes.Status200OK) =>
        page.Partial(Toast.OutOfBandPartial, toast, statusCode);

    /// <summary>A redirect that htmx follows with a full page load, or a normal redirect without htmx.</summary>
    public static IActionResult HtmxRedirect(this PageModel page, string url) => HtmxRedirect(page.HttpContext, url);

    /// <summary>htmx would swap the target of a redirect into the page, so an htmx request gets an
    /// <c>HX-Redirect</c> header instead.</summary>
    public static IActionResult HtmxRedirect(HttpContext httpContext, string url)
    {
        if (!httpContext.Request.IsHtmx())
        {
            return new RedirectResult(url);
        }

        httpContext.Response.Headers[HtmxRequestExtensions.RedirectHeader] = url;
        return new NoContentResult();
    }
}
