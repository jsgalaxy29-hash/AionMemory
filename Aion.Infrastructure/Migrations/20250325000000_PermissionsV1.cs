using System;
using Aion.Domain;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PermissionsV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TableId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_UserId",
                table: "Permissions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_UserId_Action",
                table: "Permissions",
                columns: new[] { "UserId", "Action" });

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_TableId_RecordId",
                table: "Permissions",
                columns: new[] { "TableId", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_Roles_UserId_Kind",
                table: "Roles",
                columns: new[] { "UserId", "Kind" },
                unique: true);

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "UserId", "Kind" },
                values: new object[] { Guid.Parse("00000000-0000-0000-0000-0000000000aa"), AuthorizationDefaults.AdminUserId, RoleKind.Admin.ToString() });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Permissions");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
