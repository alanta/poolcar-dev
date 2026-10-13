using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PoolCar.Reservations.Data;

/// <summary>Creates the context for <c>dotnet ef</c> without a running database.</summary>
internal sealed class ReservationsDbContextFactory : IDesignTimeDbContextFactory<ReservationsDbContext>
{
    public ReservationsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ReservationsDbContext>()
            .UseNpgsql("Host=localhost;Database=reservations")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ReservationsDbContext(options);
    }
}
