using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;
using PoolCar.Web.Employees;
using PoolCar.Web.Htmx;

namespace PoolCar.Web.Onboarding;

/// <summary>Sends signed-in users without an employee record to the welcome page, which returns them to the page
/// they asked for.</summary>
public sealed class OnboardingPageFilter(CurrentEmployee currentEmployee) : IAsyncPageFilter
{
    public const string WelcomePath = "/welkom";

    private static readonly PathString[] ExcludedPaths = [WelcomePath, "/fout", "/health", "/alive", "/.auth"];

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (currentEmployee.IsAuthenticated && currentEmployee.Id != Guid.Empty && !IsExcluded(request.Path))
        {
            await currentEmployee.LoadAsync(context.HttpContext.RequestAborted);
            if (!currentEmployee.IsKnown)
            {
                context.Result = PageModelHtmxExtensions.HtmxRedirect(context.HttpContext, GetWelcomeUrl(request));
                return;
            }
        }

        await next();
    }

    /// <summary>The welcome page, the error page, the health endpoints, the sign-in endpoints and static files never redirect.</summary>
    public static bool IsExcluded(PathString path) =>
        ExcludedPaths.Any(excluded => path.StartsWithSegments(excluded, StringComparison.OrdinalIgnoreCase))
        || Path.HasExtension(path.Value);

    public static string GetWelcomeUrl(HttpRequest request) =>
        QueryHelpers.AddQueryString(
            request.PathBase + WelcomePath,
            "returnUrl",
            request.PathBase + request.Path + request.QueryString);
}
