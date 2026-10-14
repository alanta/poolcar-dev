using PoolCar.Web.Clients;

namespace PoolCar.Web.Outages;

/// <summary>Answers a request that failed because a backend service is unavailable with an empty 503, which the
/// status code pages show as the error page, or as a toast for an htmx request.</summary>
public sealed class ServiceUnavailableMiddleware(RequestDelegate next, ILogger<ServiceUnavailableMiddleware> logger)
{
    /// <summary>Marks the app's own 503 response, so htmx swaps it but not a 503 from the ingress.</summary>
    public const string MarkerHeader = "X-PoolCar-Unavailable";

    private const string ServiceKey = "PoolCar.UnavailableService";

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception) when (!context.Response.HasStarted
            && ServiceUnavailableException.Find(exception) is { } unavailable)
        {
            // Polly already logs failed attempts with their stack traces, so only the cause is logged here
            var cause = unavailable.InnerException ?? unavailable;
            logger.LogWarning(
                "The {Service} API is unavailable for {Method} {Path}: {ExceptionType}: {ExceptionMessage}",
                unavailable.Service,
                context.Request.Method,
                context.Request.Path,
                cause.GetType().Name,
                cause.Message);

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Items[ServiceKey] = unavailable.Service;
        }
    }

    /// <summary>The service that was unavailable for this request, or null.</summary>
    public static BackendService? GetUnavailableService(HttpContext context) =>
        context.Items.TryGetValue(ServiceKey, out var service) ? service as BackendService? : null;
}
