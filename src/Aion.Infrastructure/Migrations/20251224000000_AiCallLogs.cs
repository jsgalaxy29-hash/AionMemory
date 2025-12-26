using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AiCallLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiCallLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Operation = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Tokens = table.Column<long>(type: "INTEGER", nullable: true),
                    Cost = table.Column<double>(type: "REAL", nullable: true),
                    DurationMs = table.Column<double>(type: "REAL", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCallLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiCallLogs_Provider_Model_OccurredAt",
                table: "AiCallLogs",
                columns: new[] { "Provider", "Model", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiCallLogs_Status_OccurredAt",
                table: "AiCallLogs",
                columns: new[] { "Status", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiCallLogs_WorkspaceId_OccurredAt",
                table: "AiCallLogs",
                columns: new[] { "WorkspaceId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiCallLogs");
        }
    }
}
