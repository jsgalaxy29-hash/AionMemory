using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    EnqueuedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    Length = table.Column<long>(type: "INTEGER", nullable: true),
                    Hash = table.Column<string>(type: "TEXT", nullable: true),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncOutbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncOutbox_Status_EnqueuedAt",
                table: "SyncOutbox",
                columns: new[] { "Status", "EnqueuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncOutbox_WorkspaceId",
                table: "SyncOutbox",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncOutbox");
        }
    }
}
