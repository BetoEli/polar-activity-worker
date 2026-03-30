using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WidenPolarIdsToBigint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PolarLinks.PolarID is the primary key — SQL Server requires dropping the PK
            // constraint before altering the column type, then recreating it.
            migrationBuilder.Sql(
                "ALTER TABLE [dbo].[PolarLinks] DROP CONSTRAINT [PK_PolarLinks]");

            migrationBuilder.Sql(
                "ALTER TABLE [dbo].[PolarLinks] ALTER COLUMN [PolarID] bigint NOT NULL");

            migrationBuilder.Sql(
                "ALTER TABLE [dbo].[PolarLinks] ADD CONSTRAINT [PK_PolarLinks] PRIMARY KEY ([PolarID])");

            // PolarTransactions.PolarID is a regular column (not FK-constrained).
            migrationBuilder.AlterColumn<long>(
                name: "PolarID",
                table: "PolarTransactions",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE [dbo].[PolarLinks] DROP CONSTRAINT [PK_PolarLinks]");

            migrationBuilder.Sql(
                "ALTER TABLE [dbo].[PolarLinks] ALTER COLUMN [PolarID] int NOT NULL");

            migrationBuilder.Sql(
                "ALTER TABLE [dbo].[PolarLinks] ADD CONSTRAINT [PK_PolarLinks] PRIMARY KEY ([PolarID])");

            migrationBuilder.AlterColumn<int>(
                name: "PolarID",
                table: "PolarTransactions",
                type: "int",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}
