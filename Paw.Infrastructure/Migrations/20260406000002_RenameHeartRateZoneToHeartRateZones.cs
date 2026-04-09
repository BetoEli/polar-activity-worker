using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paw.Infrastructure.Migrations;

public partial class RenameHeartRateZoneToHeartRateZones : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("EXEC sp_rename 'dbo.HeartRateZone', 'HeartRateZones'");
        migrationBuilder.Sql("EXEC sp_rename 'dbo.HeartRateZones.PK_HeartRateZone', 'PK_HeartRateZones', 'OBJECT'");
        migrationBuilder.Sql("EXEC sp_rename 'dbo.HeartRateZones.FK_HeartRateZone_Activity_ActivityID', 'FK_HeartRateZones_Activity_ActivityID', 'OBJECT'");
        migrationBuilder.Sql("EXEC sp_rename 'dbo.HeartRateZones.IX_HeartRateZone_ActivityID', 'IX_HeartRateZones_ActivityID', 'INDEX'");

        // Add IX_HeartRateZones_EntityID — defined in PawDbContext but missing from all prior migrations
        migrationBuilder.CreateIndex(
            name: "IX_HeartRateZones_EntityID",
            table: "HeartRateZones",
            column: "EntityID");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_HeartRateZones_EntityID", table: "HeartRateZones");
        migrationBuilder.Sql("EXEC sp_rename 'dbo.HeartRateZones.IX_HeartRateZones_ActivityID', 'IX_HeartRateZone_ActivityID', 'INDEX'");
        migrationBuilder.Sql("EXEC sp_rename 'dbo.HeartRateZones.FK_HeartRateZones_Activity_ActivityID', 'FK_HeartRateZone_Activity_ActivityID', 'OBJECT'");
        migrationBuilder.Sql("EXEC sp_rename 'dbo.HeartRateZones.PK_HeartRateZones', 'PK_HeartRateZone', 'OBJECT'");
        migrationBuilder.Sql("EXEC sp_rename 'dbo.HeartRateZones', 'HeartRateZone'");
    }
}
