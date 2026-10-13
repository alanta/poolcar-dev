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
    public async Task The_service_migrates_its_database_when_enabled_and_reports_healthy()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder
                .UseSetting("ConnectionStrings:reservations", connectionString)
                .UseSetting("Database:Migrate", "true"));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReservationsDbContext>();
        (await db.Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
        (await db.Employees.CountAsync(ct)).ShouldBe(30);
        (await db.Reservations.CountAsync(ct)).ShouldBe(0);
    }

    [Fact]
    public async Task The_service_leaves_its_database_alone_by_default()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:reservations", connectionString));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/ping", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReservationsDbContext>();
        (await db.Database.GetAppliedMigrationsAsync(ct)).ShouldBeEmpty();
    }
}
