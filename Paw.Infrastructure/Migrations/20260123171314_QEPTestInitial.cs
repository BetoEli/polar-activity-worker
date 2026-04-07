using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QEPTestInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PolarLinks",
                schema: "dbo",
                columns: table => new
                {
                    PolarID = table.Column<int>(type: "int", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    PersonID = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    DeviceType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    TargetZone = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    AccessToken = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolarLinks", x => x.PolarID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PolarLinks_Email",
                schema: "dbo",
                table: "PolarLinks",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_PolarLinks_PersonID",
                schema: "dbo",
                table: "PolarLinks",
                column: "PersonID");

            migrationBuilder.CreateTable(
                name: "WebhookEvents",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Provider = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExternalUserId = table.Column<long>(type: "bigint", nullable: false),
                    EntityID = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    EventTimestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResourceUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Pending"),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RawPayload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvents_Provider_ExternalUserId_EntityID",
                schema: "dbo",
                table: "WebhookEvents",
                columns: new[] { "Provider", "ExternalUserId", "EntityID" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvents_Status_ReceivedAtUtc",
                schema: "dbo",
                table: "WebhookEvents",
                columns: new[] { "Status", "ReceivedAtUtc" });

            migrationBuilder.CreateTable(
                name: "ActivityType",
                schema: "dbo",
                columns: table => new
                {
                    ActivityTypeID = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Units = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    AutomatedEntry = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    InformationID = table.Column<int>(type: "int", nullable: true),
                    InformationURL = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    InformationIsID = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    InformationIsURL = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityType", x => x.ActivityTypeID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityType_Description",
                schema: "dbo",
                table: "ActivityType",
                column: "Description");

            // Seed common Polar sport types. ID=2 ("OTHER") is the hard-coded fallback in PolarToQepMapper.
            migrationBuilder.InsertData(
                schema: "dbo",
                table: "ActivityType",
                columns: new[] { "ActivityTypeID", "Description", "Units", "Active", "AutomatedEntry", "InformationIsID", "InformationIsURL" },
                values: new object[,]
                {
                    { 1,  "RUNNING",        "BPM", true, true, false, false },
                    { 2,  "OTHER",          "BPM", true, true, false, false },
                    { 3,  "CYCLING",        "BPM", true, true, false, false },
                    { 4,  "WALKING",        "BPM", true, true, false, false },
                    { 5,  "SWIMMING",       "BPM", true, true, false, false },
                    { 6,  "STRENGTH",       "BPM", true, true, false, false },
                    { 7,  "YOGA",           "BPM", true, true, false, false },
                    { 8,  "HIKING",         "BPM", true, true, false, false },
                    { 9,  "CROSS_TRAINING", "BPM", true, true, false, false },
                    { 10, "PILATES",        "BPM", true, true, false, false }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ActivityType", schema: "dbo");
            migrationBuilder.DropTable(name: "WebhookEvents", schema: "dbo");
            migrationBuilder.DropTable(name: "PolarLinks", schema: "dbo");
        }
    }
}
