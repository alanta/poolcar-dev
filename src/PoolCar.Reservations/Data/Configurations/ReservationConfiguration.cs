using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Data.Configurations;

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("reservations");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.CarId).HasMaxLength(10);

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(r => r.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.EmployeeId);

        // Periods reference the reservation together with its car, so their denormalised car_id
        // always matches the reservation.
        builder.HasAlternateKey(r => new { r.Id, r.CarId });
        builder.HasMany(r => r.Periods)
            .WithOne()
            .HasForeignKey(ReservationPeriodConfiguration.ReservationId, ReservationPeriodConfiguration.CarId)
            .HasPrincipalKey(r => new { r.Id, r.CarId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(r => r.Periods)
            .HasField("_periods")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
