using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MemoryInsights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MemoryInsights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RecordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    TopicsJson = table.Column<string>(type: "TEXT", nullable: false),
                    SuggestedLinksJson = table.Column<string>(type: "TEXT", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoryInsights", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryInsights_GeneratedAt",
                table: "MemoryInsights",
                column: "GeneratedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemoryInsights");
        }
    }
}
