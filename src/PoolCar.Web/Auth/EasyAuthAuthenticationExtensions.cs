using Microsoft.AspNetCore.Authentication;

namespace PoolCar.Web.Auth;

public static class EasyAuthAuthenticationExtensions
{
    public static AuthenticationBuilder AddEasyAuth(
        this AuthenticationBuilder builder,
        string scheme = EasyAuthAuthenticationDefaults.AuthenticationScheme)
    {
        return builder.AddScheme<AuthenticationSchemeOptions, EasyAuthAuthenticationHandler>(scheme, null);
    }
}
