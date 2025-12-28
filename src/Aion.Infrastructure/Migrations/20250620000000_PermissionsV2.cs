using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PermissionsV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Permissions_TableId_RecordId",
                table: "Permissions");

            migrationBuilder.AddColumn<string>(
                name: "FieldName",
                table: "Permissions",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "GrantedAt",
                table: "Permissions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GrantedByUserId",
                table: "Permissions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_TableId_RecordId_FieldName",
                table: "Permissions",
                columns: new[] { "TableId", "RecordId", "FieldName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Permissions_TableId_RecordId_FieldName",
                table: "Permissions");

            migrationBuilder.DropColumn(
                name: "FieldName",
                table: "Permissions");

            migrationBuilder.DropColumn(
                name: "GrantedAt",
                table: "Permissions");

            migrationBuilder.DropColumn(
                name: "GrantedByUserId",
                table: "Permissions");

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_TableId_RecordId",
                table: "Permissions",
                columns: new[] { "TableId", "RecordId" });
        }
    }
}
