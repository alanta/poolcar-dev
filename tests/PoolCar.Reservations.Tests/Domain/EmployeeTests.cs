using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Tests.Domain;

public class EmployeeTests
{
    [Fact]
    public void Create_sets_all_properties()
    {
        var id = Guid.NewGuid();

        var result = Employee.Create(id, " Anna de Vries ", "utrecht", EmployeeKind.SignedIn);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe(id);
        result.Value.Name.ShouldBe("Anna de Vries");
        result.Value.HomeOffice.ShouldBe("utrecht");
        result.Value.Kind.ShouldBe(EmployeeKind.SignedIn);
    }

    [Theory]
    [InlineData("", "utrecht")]
    [InlineData("Anna", " ")]
    public void Create_without_name_or_office_is_invalid(string name, string homeOffice)
    {
        var result = Employee.Create(Guid.NewGuid(), name, homeOffice, EmployeeKind.Seeded);

        result.Error.ShouldNotBeNull().Code.ShouldBe(DomainError.InvalidCode);
    }

    [Fact]
    public void Create_without_id_is_invalid()
    {
        Employee.Create(Guid.Empty, "Anna", "utrecht", EmployeeKind.Seeded).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void Update_changes_name_and_home_office()
    {
        var employee = Employee.Create(Guid.NewGuid(), "Anna", "utrecht", EmployeeKind.Seeded).Value!;

        employee.Update("Anna Jansen", "eindhoven").IsSuccess.ShouldBeTrue();

        employee.Name.ShouldBe("Anna Jansen");
        employee.HomeOffice.ShouldBe("eindhoven");
    }

    [Fact]
    public void Invalid_update_keeps_the_employee_unchanged()
    {
        var employee = Employee.Create(Guid.NewGuid(), "Anna", "utrecht", EmployeeKind.Seeded).Value!;

        employee.Update("", "amsterdam").Error.ShouldNotBeNull().Code.ShouldBe(DomainError.InvalidCode);

        employee.Name.ShouldBe("Anna");
        employee.HomeOffice.ShouldBe("utrecht");
    }
}
