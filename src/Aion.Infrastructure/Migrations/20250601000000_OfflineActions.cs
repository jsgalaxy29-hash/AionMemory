using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations;

public partial class OfflineActions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "OfflineActions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                TableId = table.Column<Guid>(type: "TEXT", nullable: false),
                RecordId = table.Column<Guid>(type: "TEXT", nullable: false),
                Action = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                EnqueuedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                AppliedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                FailureReason = table.Column<string>(type: "TEXT", nullable: true),
                WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OfflineActions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_OfflineActions_Status_EnqueuedAt",
            table: "OfflineActions",
            columns: new[] { "Status", "EnqueuedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_OfflineActions_TableId_RecordId",
            table: "OfflineActions",
            columns: new[] { "TableId", "RecordId" });

        migrationBuilder.CreateIndex(
            name: "IX_OfflineActions_WorkspaceId",
            table: "OfflineActions",
            column: "WorkspaceId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "OfflineActions");
    }
}
