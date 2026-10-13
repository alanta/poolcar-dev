using PoolCar.Web.Auth;
using PoolCar.Web.Clients;
using PoolCar.Web.Employees;
using PoolCar.Web.Tests.Support;
using Shouldly;

namespace PoolCar.Web.Tests;

public class CurrentEmployeeTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task KnownEmployee_UsesEmployeeRecord()
    {
        var handler = EmployeeHandler();
        var current = Create(TestPrincipals.Create(TestPrincipals.KnownId, "bram@example.test"), handler);

        await current.LoadAsync(Token);
        await current.LoadAsync(Token);

        current.IsKnown.ShouldBeTrue();
        current.Id.ShouldBe(TestPrincipals.KnownId);
        current.Name.ShouldBe("Bram Testbeek");
        current.FirstName.ShouldBe("Bram");
        current.HomeOffice.ShouldBe("utrecht");
        current.IsOperator.ShouldBeFalse();
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task UnknownEmployee_FallsBackToSignInName()
    {
        var current = Create(TestPrincipals.Create(TestPrincipals.UnknownId, "Jan de Boer"), EmployeeHandler());

        await current.LoadAsync(Token);

        current.IsKnown.ShouldBeFalse();
        current.Employee.ShouldBeNull();
        current.Id.ShouldBe(TestPrincipals.UnknownId);
        current.Name.ShouldBe("Jan de Boer");
        current.FirstName.ShouldBe("Jan");
        current.HomeOffice.ShouldBeNull();
    }

    [Fact]
    public async Task Operator_IsOperator()
    {
        var current = Create(
            TestPrincipals.Create(TestPrincipals.KnownId, roles: PoolCarRoles.FleetOperator),
            EmployeeHandler());

        await current.LoadAsync(Token);

        current.IsOperator.ShouldBeTrue();
        current.IsKnown.ShouldBeTrue();
    }

    [Fact]
    public async Task Anonymous_IsNotKnown_WithoutCallingTheApi()
    {
        var handler = EmployeeHandler();
        var current = Create(null, handler);

        await current.LoadAsync(Token);

        current.IsAuthenticated.ShouldBeFalse();
        current.Id.ShouldBe(Guid.Empty);
        current.IsKnown.ShouldBeFalse();
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public void ReadingEmployee_BeforeLoading_Throws()
    {
        var current = Create(TestPrincipals.Create(TestPrincipals.KnownId), EmployeeHandler());

        Should.Throw<InvalidOperationException>(() => current.IsKnown);
    }

    [Fact]
    public async Task Set_ReplacesCachedEmployee()
    {
        var current = Create(TestPrincipals.Create(TestPrincipals.UnknownId), EmployeeHandler());
        await current.LoadAsync(Token);

        current.Set(new Employee(TestPrincipals.UnknownId, "Jan de Boer", "eindhoven", EmployeeKind.SignedIn));

        current.IsKnown.ShouldBeTrue();
        current.HomeOffice.ShouldBe("eindhoven");
    }

    [Theory]
    [InlineData("Jan de Boer", "Jan")]
    [InlineData("  fleur  ", "Fleur")]
    [InlineData("demo@example.test", "Demo")]
    [InlineData("jan.de.boer@example.test", "Jan")]
    [InlineData("", "")]
    public void GetFirstName_TakesFirstWord(string name, string expected)
    {
        CurrentEmployee.GetFirstName(name).ShouldBe(expected);
    }

    private static StubHttpHandler EmployeeHandler() => new StubHttpHandler().OnJson(
        HttpMethod.Get,
        $"/employees/{TestPrincipals.KnownId}",
        $$"""{"id":"{{TestPrincipals.KnownId}}","name":"Bram Testbeek","homeOffice":"utrecht","kind":"Seeded"}""");

    private static CurrentEmployee Create(System.Security.Claims.ClaimsPrincipal? user, StubHttpHandler handler) =>
        new(
            TestPrincipals.Accessor(user),
            new ReservationsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://reservations") }));
}
