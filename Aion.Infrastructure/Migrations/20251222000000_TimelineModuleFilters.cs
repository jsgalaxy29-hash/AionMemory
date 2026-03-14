using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TimelineModuleFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ModuleId",
                table: "HistoryEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HistoryEvents_ModuleId_OccurredAt",
                table: "HistoryEvents",
                columns: new[] { "ModuleId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HistoryEvents_ModuleId_OccurredAt",
                table: "HistoryEvents");

            migrationBuilder.DropColumn(
                name: "ModuleId",
                table: "HistoryEvents");
        }
    }
}
