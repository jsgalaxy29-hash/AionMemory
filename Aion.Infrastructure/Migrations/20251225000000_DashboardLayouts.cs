using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DashboardLayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DashboardLayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DashboardKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    LayoutJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardLayouts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DashboardLayouts_DashboardKey",
                table: "DashboardLayouts",
                column: "DashboardKey");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardLayouts_WorkspaceId",
                table: "DashboardLayouts",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DashboardLayouts");
        }
    }
}
