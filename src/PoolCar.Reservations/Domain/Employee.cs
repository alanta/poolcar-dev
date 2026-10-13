namespace PoolCar.Reservations.Domain;

public sealed class Employee
{
    private Employee()
    {
        Name = string.Empty;
        HomeOffice = string.Empty;
    }

    private Employee(Guid id, string name, string homeOffice, EmployeeKind kind)
    {
        Id = id;
        Name = name;
        HomeOffice = homeOffice;
        Kind = kind;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    /// <summary>The id of the office the employee usually works from, such as <c>utrecht</c>.</summary>
    public string HomeOffice { get; private set; }

    public EmployeeKind Kind { get; private set; }

    public static Result<Employee> Create(Guid id, string name, string homeOffice, EmployeeKind kind)
    {
        if (id == Guid.Empty)
        {
            return DomainError.Invalid("Onbekende medewerker.");
        }

        var error = Validate(name, homeOffice);
        if (error is not null)
        {
            return error;
        }

        return new Employee(id, name.Trim(), homeOffice.Trim(), kind);
    }

    public Result Update(string name, string homeOffice)
    {
        var error = Validate(name, homeOffice);
        if (error is not null)
        {
            return error;
        }

        Name = name.Trim();
        HomeOffice = homeOffice.Trim();
        return Result.Success();
    }

    private static DomainError? Validate(string name, string homeOffice)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return DomainError.Invalid("Vul je naam in.");
        }

        if (string.IsNullOrWhiteSpace(homeOffice))
        {
            return DomainError.Invalid("Kies je kantoor.");
        }

        return null;
    }
}
