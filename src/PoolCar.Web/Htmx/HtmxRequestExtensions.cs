namespace PoolCar.Web.Htmx;

public static class HtmxRequestExtensions
{
    public const string RequestHeader = "HX-Request";
    public const string BoostedHeader = "HX-Boosted";
    public const string RedirectHeader = "HX-Redirect";
    public const string HistoryRestoreHeader = "HX-History-Restore-Request";
    public const string ReswapHeader = "HX-Reswap";
    public const string PushUrlHeader = "HX-Push-Url";

    /// <summary>Whether htmx sent the request, including boosted links and forms.</summary>
    public static bool IsHtmx(this HttpRequest request) => IsTrue(request, RequestHeader);

    /// <summary>Whether the request comes from a boosted link or form, which expects a full page.</summary>
    public static bool IsHtmxBoosted(this HttpRequest request) => IsTrue(request, BoostedHeader);

    /// <summary>Whether htmx sent the request to swap part of the page, so a partial is enough. Restoring a page
    /// from history after a pushed URL needs the full page.</summary>
    public static bool IsHtmxPartial(this HttpRequest request) =>
        request.IsHtmx() && !request.IsHtmxBoosted() && !IsTrue(request, HistoryRestoreHeader);

    private static bool IsTrue(HttpRequest request, string header) =>
        string.Equals(request.Headers[header], "true", StringComparison.OrdinalIgnoreCase);
}
