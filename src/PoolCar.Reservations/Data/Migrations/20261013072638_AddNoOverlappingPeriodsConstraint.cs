using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PoolCar.Reservations.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNoOverlappingPeriodsConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql(
                """
                ALTER TABLE reservation_periods ADD CONSTRAINT no_overlapping_periods
                  EXCLUDE USING gist (car_id WITH =, tstzrange(starts_at, ends_at, '[)') WITH &&)
                  WHERE (status IN ('Scheduled', 'PickedUp'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE reservation_periods DROP CONSTRAINT no_overlapping_periods;");
        }
    }
}
