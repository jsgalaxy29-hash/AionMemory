using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModuleSchemaVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModuleSchemaVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ModuleSlug = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    SpecHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModuleSchemaVersions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModuleSchemaVersions_ModuleSlug_IsActive",
                table: "ModuleSchemaVersions",
                columns: new[] { "ModuleSlug", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ModuleSchemaVersions_ModuleSlug_Version",
                table: "ModuleSchemaVersions",
                columns: new[] { "ModuleSlug", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModuleSchemaVersions_WorkspaceId",
                table: "ModuleSchemaVersions",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModuleSchemaVersions");
        }
    }
}
