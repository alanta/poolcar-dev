using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace PoolCar.Web.Auth;

public static class EasyAuthPrincipalParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static ClaimsPrincipal? Parse(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(headerValue);
            var payload = JsonSerializer.Deserialize<MsClientPrincipalPayload>(bytes, JsonOptions);
            if (payload is null)
            {
                return null;
            }

            return CreatePrincipal(payload);
        }
        catch
        {
            return null;
        }
    }

    public static bool TryParse(string? headerValue, out ClaimsPrincipal? principal)
    {
        principal = Parse(headerValue);
        return principal is not null;
    }

    public static string CreateHeader(
        string? name = null,
        string? objectId = null,
        IEnumerable<string>? roles = null,
        string? preferredUsername = null,
        string? authType = "aad")
    {
        var claims = new List<MsClientClaim>();

        if (!string.IsNullOrEmpty(name))
        {
            claims.Add(new MsClientClaim { Type = EasyAuthClaimTypes.Name, Value = name });
        }

        if (!string.IsNullOrEmpty(preferredUsername))
        {
            claims.Add(new MsClientClaim { Type = EasyAuthClaimTypes.PreferredUsername, Value = preferredUsername });
        }

        if (!string.IsNullOrEmpty(objectId))
        {
            claims.Add(new MsClientClaim { Type = EasyAuthClaimTypes.ObjectId, Value = objectId });
        }

        if (roles != null)
        {
            foreach (var role in roles)
            {
                if (!string.IsNullOrEmpty(role))
                {
                    claims.Add(new MsClientClaim { Type = EasyAuthClaimTypes.Roles, Value = role });
                }
            }
        }

        var payload = new MsClientPrincipalPayload
        {
            AuthenticationType = authType,
            NameType = EasyAuthClaimTypes.Name,
            RoleType = EasyAuthClaimTypes.Roles,
            Claims = claims
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static ClaimsPrincipal CreatePrincipal(MsClientPrincipalPayload payload)
    {
        var claims = new List<Claim>();

        if (payload.Claims != null)
        {
            foreach (var claim in payload.Claims)
            {
                if (!string.IsNullOrEmpty(claim.Type) && claim.Value != null)
                {
                    claims.Add(new Claim(claim.Type, claim.Value));
                }
            }
        }

        // "name claim is name, falling back to preferred_username"
        var nameClaim = claims.FirstOrDefault(c => c.Type == EasyAuthClaimTypes.Name);
        if (nameClaim is null)
        {
            var preferredUsername = claims.FirstOrDefault(c => c.Type == EasyAuthClaimTypes.PreferredUsername);
            if (preferredUsername is not null)
            {
                claims.Add(new Claim(EasyAuthClaimTypes.Name, preferredUsername.Value));
            }
        }

        // Map roles to standard ClaimTypes.Role as well for framework compatibility
        var roleClaims = claims.Where(c => c.Type is EasyAuthClaimTypes.Roles or "role" or ClaimTypes.Role).ToList();
        foreach (var roleClaim in roleClaims)
        {
            if (!claims.Any(c => c.Type == EasyAuthClaimTypes.Roles && c.Value == roleClaim.Value))
            {
                claims.Add(new Claim(EasyAuthClaimTypes.Roles, roleClaim.Value));
            }
            if (!claims.Any(c => c.Type == ClaimTypes.Role && c.Value == roleClaim.Value))
            {
                claims.Add(new Claim(ClaimTypes.Role, roleClaim.Value));
            }
        }

        var authType = !string.IsNullOrWhiteSpace(payload.AuthenticationType)
            ? payload.AuthenticationType
            : EasyAuthAuthenticationDefaults.AuthenticationScheme;

        var identity = new ClaimsIdentity(
            claims,
            authenticationType: authType,
            nameType: EasyAuthClaimTypes.Name,
            roleType: EasyAuthClaimTypes.Roles);

        return new ClaimsPrincipal(identity);
    }
}
