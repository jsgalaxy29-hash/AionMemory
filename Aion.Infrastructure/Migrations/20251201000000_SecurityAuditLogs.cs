using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SecurityAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SecurityAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TargetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditLogs_Category_OccurredAt",
                table: "SecurityAuditLogs",
                columns: new[] { "Category", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditLogs_WorkspaceId_OccurredAt",
                table: "SecurityAuditLogs",
                columns: new[] { "WorkspaceId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SecurityAuditLogs");
        }
    }
}
