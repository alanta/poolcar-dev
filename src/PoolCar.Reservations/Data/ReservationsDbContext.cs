using Microsoft.EntityFrameworkCore;
using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Data;

public sealed class ReservationsDbContext(DbContextOptions<ReservationsDbContext> options) : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    public DbSet<ReservationPeriod> ReservationPeriods => Set<ReservationPeriod>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
        configurationBuilder.Properties<DayPart>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<EmployeeKind>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<PeriodStatus>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReservationsDbContext).Assembly);
}
