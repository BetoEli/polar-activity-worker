using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paw.Infrastructure.Migrations;

public partial class AddWebhookRetryColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "RetryCount",
            table: "WebhookEvents",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTime>(
            name: "NextRetryAt",
            table: "WebhookEvents",
            type: "datetime2",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "RetryCount", table: "WebhookEvents");
        migrationBuilder.DropColumn(name: "NextRetryAt", table: "WebhookEvents");
    }
}
