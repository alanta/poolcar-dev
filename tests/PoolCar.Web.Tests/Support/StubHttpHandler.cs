using System.Net;
using System.Text;

namespace PoolCar.Web.Tests.Support;

/// <summary>Answers HTTP requests from a list of routes and records every request.</summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly List<(HttpMethod Method, string Path, Func<RecordedRequest, HttpResponseMessage> Respond)> _routes = [];
    private readonly List<RecordedRequest> _requests = [];

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>Adds a route; <paramref name="path"/> is matched against the path without the query.</summary>
    public StubHttpHandler On(HttpMethod method, string path, Func<RecordedRequest, HttpResponseMessage> respond)
    {
        _routes.Add((method, path, respond));
        return this;
    }

    public StubHttpHandler OnJson(HttpMethod method, string path, string json, HttpStatusCode status = HttpStatusCode.OK) =>
        On(method, path, _ => Json(json, status));

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Problem(HttpStatusCode status, string code, string detail, string extra = "") =>
        new(status)
        {
            Content = new StringContent(
                $$"""{"status":{{(int)status}},"title":"Fout","detail":"{{detail}}","code":"{{code}}"{{extra}}}""",
                Encoding.UTF8,
                "application/problem+json"),
        };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri!,
            body,
            request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase));
        lock (_requests)
        {
            _requests.Add(recorded);
        }

        var route = _routes.LastOrDefault(r => r.Method == request.Method && r.Path == request.RequestUri!.AbsolutePath);
        return route.Respond is null
            ? Problem(HttpStatusCode.NotFound, "not-found", "Niet gevonden.")
            : route.Respond(recorded);
    }
}

public sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    string? Body,
    IReadOnlyDictionary<string, string> Headers)
{
    public string PathAndQuery => Uri.PathAndQuery;
}
