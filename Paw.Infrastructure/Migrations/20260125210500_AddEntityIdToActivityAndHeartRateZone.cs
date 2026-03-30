using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEntityIDToActivityAndHeartRateZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Activity",
                columns: table => new
                {
                    ActivityID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityID = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ActivityTypeID = table.Column<int>(type: "int", nullable: true),
                    UserID = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Username = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Measurement = table.Column<double>(type: "float", nullable: true),
                    Minutes = table.Column<int>(type: "int", nullable: true),
                    Duration = table.Column<double>(type: "float", nullable: true),
                    Distance = table.Column<double>(type: "float", nullable: true),
                    AerobicPoints = table.Column<int>(type: "int", nullable: true),
                    DateDone = table.Column<DateTime>(type: "datetime", nullable: true),
                    DateEntered = table.Column<DateTime>(type: "datetime", nullable: true),
                    DeviceType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    TargetZone = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Activity", x => x.ActivityID);
                });

            migrationBuilder.CreateTable(
                name: "PolarTransactions",
                columns: table => new
                {
                    PolarTransactionID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PolarID = table.Column<int>(type: "int", nullable: false),
                    FirstTouched = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastTouched = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Response = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsCommitted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsProcessed = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Attempt = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolarTransactions", x => x.PolarTransactionID);
                });

            migrationBuilder.CreateTable(
                name: "HeartRateZone",
                columns: table => new
                {
                    HeartRateZoneID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityID = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ActivityID = table.Column<long>(type: "bigint", nullable: false),
                    Zone = table.Column<int>(type: "int", nullable: true),
                    Lower = table.Column<int>(type: "int", nullable: true),
                    Upper = table.Column<int>(type: "int", nullable: true),
                    Duration = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeartRateZone", x => x.HeartRateZoneID);
                    table.ForeignKey(
                        name: "FK_HeartRateZone_Activity_ActivityID",
                        column: x => x.ActivityID,
                        principalTable: "Activity",
                        principalColumn: "ActivityID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Activity_DateDone",
                table: "Activity",
                column: "DateDone");

            migrationBuilder.CreateIndex(
                name: "IX_Activity_UserID",
                table: "Activity",
                column: "UserID");

            migrationBuilder.CreateIndex(
                name: "IX_HeartRateZone_ActivityID",
                table: "HeartRateZone",
                column: "ActivityID");

            migrationBuilder.CreateIndex(
                name: "IX_PolarTransactions_Location",
                table: "PolarTransactions",
                column: "Location");

            migrationBuilder.CreateIndex(
                name: "IX_PolarTransactions_PolarID",
                table: "PolarTransactions",
                column: "PolarID");

            migrationBuilder.CreateIndex(
                name: "IX_PolarTransactions_PolarID_IsProcessed",
                table: "PolarTransactions",
                columns: new[] { "PolarID", "IsProcessed" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HeartRateZone");

            migrationBuilder.DropTable(
                name: "PolarTransactions");

            migrationBuilder.DropTable(
                name: "Activity");
        }
    }
}
