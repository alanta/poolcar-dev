using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Data.Configurations;

internal sealed class ReservationPeriodConfiguration : IEntityTypeConfiguration<ReservationPeriod>
{
    public const string ReservationId = "ReservationId";
    public const string CarId = "CarId";

    public void Configure(EntityTypeBuilder<ReservationPeriod> builder)
    {
        builder.ToTable("reservation_periods");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property<Guid>(ReservationId);
        builder.Property<string>(CarId).HasMaxLength(10).IsRequired();
        builder.Property(p => p.Start).HasColumnName("starts_at");
        builder.Property(p => p.End).HasColumnName("ends_at");

        builder.HasIndex(CarId, nameof(ReservationPeriod.Start));
    }
}
