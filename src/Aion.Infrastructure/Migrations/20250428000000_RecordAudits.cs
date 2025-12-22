using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RecordAudits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecordAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityTypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChangeType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 1L),
                    DataJson = table.Column<string>(type: "TEXT", nullable: false),
                    PreviousDataJson = table.Column<string>(type: "TEXT", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordAudits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecordAudits_EntityTypeId_RecordId_ChangedAt",
                table: "RecordAudits",
                columns: new[] { "EntityTypeId", "RecordId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RecordAudits_EntityTypeId_RecordId_Version",
                table: "RecordAudits",
                columns: new[] { "EntityTypeId", "RecordId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecordAudits");
        }
    }
}
