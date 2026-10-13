using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PoolCar.Reservations.Data;
using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Tests.Data;

/// <summary>A migrated database for a single test class.</summary>
public sealed class ReservationsDatabase(PostgresFixture postgres) : IAsyncLifetime
{
    private static int s_carCount;

    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        ConnectionString = await postgres.CreateDatabaseAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public ReservationsDbContext CreateContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<ReservationsDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptors)
            .Options);

    /// <summary>Returns a car id that no other test uses.</summary>
    public static string NewCarId() => $"TST{Interlocked.Increment(ref s_carCount):D6}";

    public async Task<Employee> AddEmployeeAsync()
    {
        var employee = Employee.Create(Guid.NewGuid(), "Sanne de Vries", "utrecht", EmployeeKind.SignedIn).Value!;
        await using var db = CreateContext();
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee;
    }
}
