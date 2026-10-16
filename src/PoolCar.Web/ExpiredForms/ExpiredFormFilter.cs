using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;
using PoolCar.Web.Htmx;

namespace PoolCar.Web.ExpiredForms;

/// <summary>Marks a request whose antiforgery token could not be validated, usually because the page was opened before
/// the app restarted with new data protection keys. The empty 400 is then shown by the status code pages as an expired
/// page, or as a toast for an htmx request, which asks the user to reload the page.</summary>
public sealed class ExpiredFormFilter(ILogger<ExpiredFormFilter> logger) : IAlwaysRunResultFilter
{
    /// <summary>Marks the app's own response for an expired page, so htmx swaps it but not other 400 responses.</summary>
    public const string MarkerHeader = "X-PoolCar-Expired";

    private const string ExpiredKey = "PoolCar.ExpiredForm";

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not IAntiforgeryValidationFailedResult)
        {
            return;
        }

        var request = context.HttpContext.Request;
        logger.LogWarning(
            "The antiforgery token for {Method} {Path} is missing or invalid, so the page is shown as expired. Htmx: {Htmx}, antiforgery cookie present: {HasCookie}",
            request.Method,
            request.Path,
            request.IsHtmx(),
            request.Cookies.Keys.Any(name => name.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal)));
        context.HttpContext.Items[ExpiredKey] = true;
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }

    /// <summary>Whether this request failed because its page has expired.</summary>
    public static bool IsExpired(HttpContext context) => context.Items.ContainsKey(ExpiredKey);

    /// <summary>The page the request came from, so the user can reload it, or the home page when the referrer is
    /// missing or another site. A proxy in front of the app can change the host, so the browser's
    /// <c>Sec-Fetch-Site</c> header also tells whether the referrer is this site.</summary>
    public static string GetReloadUrl(HttpRequest request)
    {
        if (!Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var referrer)
            || referrer.Scheme is not ("http" or "https"))
        {
            return "/";
        }

        var isSameOrigin = string.Equals(request.Headers["Sec-Fetch-Site"], "same-origin", StringComparison.Ordinal)
            || string.Equals(referrer.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
        return isSameOrigin && IsLocalPath(referrer.PathAndQuery) ? referrer.PathAndQuery : "/";
    }

    // A path such as //example.com or /\example.com would leave the site
    private static bool IsLocalPath(string url) =>
        url.Length > 0 && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}
