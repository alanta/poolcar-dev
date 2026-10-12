using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PoolCar.Web.Auth;

public class EasyAuthAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public EasyAuthAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(EasyAuthAuthenticationDefaults.HeaderName, out var headerValues) ||
            string.IsNullOrWhiteSpace(headerValues.ToString()))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var headerValue = headerValues.ToString();
        var principal = EasyAuthPrincipalParser.Parse(headerValue);

        if (principal is null)
        {
            return Task.FromResult(AuthenticateResult.Fail($"Invalid {EasyAuthAuthenticationDefaults.HeaderName} header"));
        }

        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var path = Request.PathBase + Request.Path + Request.QueryString;
        if (string.IsNullOrEmpty(path))
        {
            path = "/";
        }

        var redirectUrl = QueryHelpers.AddQueryString("/.auth/login/aad", "post_login_redirect_uri", path);
        Response.Redirect(redirectUrl);
        return Task.CompletedTask;
    }
}
