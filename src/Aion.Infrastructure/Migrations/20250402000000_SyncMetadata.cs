using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ModifiedAt",
                table: "Records",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Records",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ModifiedAt",
                table: "Modules",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Modules",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateIndex(
                name: "IX_Records_TableId_ModifiedAt",
                table: "Records",
                columns: new[] { "TableId", "ModifiedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Modules_ModifiedAt",
                table: "Modules",
                column: "ModifiedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Records_TableId_ModifiedAt",
                table: "Records");

            migrationBuilder.DropIndex(
                name: "IX_Modules_ModifiedAt",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "ModifiedAt",
                table: "Records");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Records");

            migrationBuilder.DropColumn(
                name: "ModifiedAt",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Modules");
        }
    }
}
