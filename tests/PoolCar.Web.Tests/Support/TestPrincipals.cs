using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PoolCar.Web.Auth;

namespace PoolCar.Web.Tests.Support;

public static class TestPrincipals
{
    public static readonly Guid KnownId = Guid.Parse("f0671165-c417-421a-9eac-649e8534de6e");
    public static readonly Guid UnknownId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");

    public static ClaimsPrincipal Create(Guid id, string name = "Jan de Boer", params string[] roles) =>
        EasyAuthPrincipalParser.Parse(EasyAuthPrincipalParser.CreateHeader(name, id.ToString(), roles))!;

    public static IHttpContextAccessor Accessor(ClaimsPrincipal? user) =>
        new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user ?? new ClaimsPrincipal() } };

    public static string Header(Guid id, string name = "Jan de Boer", params string[] roles) =>
        EasyAuthPrincipalParser.CreateHeader(name, id.ToString(), roles);
}
