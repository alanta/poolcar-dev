using PoolCar.Web.Auth;
using PoolCar.Web.Clients;
using PoolCar.Web.Tests.Support;
using Shouldly;

namespace PoolCar.Web.Tests;

public class ActingUserHandlerTests
{
    [Fact]
    public async Task Employee_SendsEmployeeHeaderOnly()
    {
        var request = await SendAsync(TestPrincipals.Create(TestPrincipals.KnownId));

        request.Headers[ActingUserHandler.EmployeeHeader].ShouldBe(TestPrincipals.KnownId.ToString());
        request.Headers.ContainsKey(ActingUserHandler.OperatorHeader).ShouldBeFalse();
    }

    [Fact]
    public async Task Operator_SendsOperatorHeader()
    {
        var request = await SendAsync(TestPrincipals.Create(TestPrincipals.KnownId, roles: PoolCarRoles.FleetOperator));

        request.Headers[ActingUserHandler.EmployeeHeader].ShouldBe(TestPrincipals.KnownId.ToString());
        request.Headers[ActingUserHandler.OperatorHeader].ShouldBe("true");
    }

    [Fact]
    public async Task Anonymous_SendsNoHeaders()
    {
        var request = await SendAsync(null);

        request.Headers.ContainsKey(ActingUserHandler.EmployeeHeader).ShouldBeFalse();
        request.Headers.ContainsKey(ActingUserHandler.OperatorHeader).ShouldBeFalse();
    }

    [Fact]
    public async Task UserWithoutObjectId_SendsNoHeaders()
    {
        var user = EasyAuthPrincipalParser.Parse(EasyAuthPrincipalParser.CreateHeader(name: "Jan", roles: [PoolCarRoles.FleetOperator]));

        var request = await SendAsync(user);

        request.Headers.ContainsKey(ActingUserHandler.EmployeeHeader).ShouldBeFalse();
        request.Headers.ContainsKey(ActingUserHandler.OperatorHeader).ShouldBeFalse();
    }

    private static async Task<RecordedRequest> SendAsync(System.Security.Claims.ClaimsPrincipal? user)
    {
        var stub = new StubHttpHandler().OnJson(HttpMethod.Get, "/reservations/mine", "[]");
        var handler = new ActingUserHandler(TestPrincipals.Accessor(user)) { InnerHandler = stub };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://reservations") };

        using var response = await client.GetAsync("/reservations/mine", TestContext.Current.CancellationToken);

        return stub.Requests.ShouldHaveSingleItem();
    }
}
