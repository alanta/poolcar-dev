using System.Security.Claims;

namespace PoolCar.Web.Auth;

public static class EasyAuthClaimsPrincipalExtensions
{
    public static string? GetName(this ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return null;
        }

        return principal.FindFirst(EasyAuthClaimTypes.Name)?.Value
            ?? principal.FindFirst(EasyAuthClaimTypes.PreferredUsername)?.Value
            ?? principal.Identity?.Name;
    }

    public static string? GetObjectId(this ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return null;
        }

        return principal.FindFirst(EasyAuthClaimTypes.ObjectId)?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    public static IReadOnlyList<string> GetRoles(this ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return [];
        }

        return principal.FindAll(c => c.Type is EasyAuthClaimTypes.Roles or "role" or ClaimTypes.Role)
            .Select(c => c.Value)
            .Distinct()
            .ToList();
    }

    public static bool IsOperator(this ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return false;
        }

        return principal.GetRoles().Contains(PoolCarRoles.FleetOperator, StringComparer.OrdinalIgnoreCase);
    }
}
