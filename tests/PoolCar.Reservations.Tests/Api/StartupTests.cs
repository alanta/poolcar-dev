using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PoolCar.Reservations.Data;
using PoolCar.Reservations.Tests.Data;

namespace PoolCar.Reservations.Tests.Api;

public class StartupTests(PostgresFixture postgres)
{
    [Fact]
    public async Task The_service_migrates_its_database_and_reports_healthy()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:reservations", connectionString));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReservationsDbContext>();
        (await db.Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
        (await db.Employees.CountAsync(ct)).ShouldBe(0);
    }
}
