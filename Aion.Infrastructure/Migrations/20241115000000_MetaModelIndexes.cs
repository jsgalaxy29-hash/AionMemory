using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MetaModelIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Tables_Name",
                table: "Tables",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TableViews_TableId_Name",
                table: "TableViews",
                columns: new[] { "TableId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TableFields_TableId_Name",
                table: "TableFields",
                columns: new[] { "TableId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tables_Name",
                table: "Tables");

            migrationBuilder.DropIndex(
                name: "IX_TableViews_TableId_Name",
                table: "TableViews");

            migrationBuilder.DropIndex(
                name: "IX_TableFields_TableId_Name",
                table: "TableFields");
        }
    }
}
