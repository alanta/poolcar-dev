namespace PoolCar.EndToEnd.Tests;

public sealed record Employee(Guid Id, string Name, string Office);

/// <summary>Employees that the Reservations service creates in an empty database.</summary>
public static class SeededEmployees
{
    public static readonly Employee Utrecht = new(Guid.Parse("f0671165-c417-421a-9eac-649e8534de6e"), "Bram Testbeek", "Utrecht");
    public static readonly Employee Amsterdam = new(Guid.Parse("2c35f465-5839-4fc9-9bd6-1c56ee4f174f"), "Fleur Proefhuis", "Amsterdam");
    public static readonly Employee Eindhoven = new(Guid.Parse("fa91db2c-71ec-4752-beb9-4dcf1e2e58d4"), "Daan Voorbeeld", "Eindhoven");
}
