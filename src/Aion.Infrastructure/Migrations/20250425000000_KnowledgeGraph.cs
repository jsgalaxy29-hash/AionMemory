using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KnowledgeGraph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KnowledgeNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TableId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeNodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeEdges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromNodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToNodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelationType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeEdges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeEdges_KnowledgeNodes_FromNodeId",
                        column: x => x.FromNodeId,
                        principalTable: "KnowledgeNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KnowledgeEdges_KnowledgeNodes_ToNodeId",
                        column: x => x.ToNodeId,
                        principalTable: "KnowledgeNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeEdges_FromNodeId_ToNodeId_RelationType",
                table: "KnowledgeEdges",
                columns: new[] { "FromNodeId", "ToNodeId", "RelationType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeEdges_RelationType",
                table: "KnowledgeEdges",
                column: "RelationType");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeEdges_ToNodeId",
                table: "KnowledgeEdges",
                column: "ToNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeNodes_TableId_RecordId",
                table: "KnowledgeNodes",
                columns: new[] { "TableId", "RecordId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KnowledgeEdges");

            migrationBuilder.DropTable(
                name: "KnowledgeNodes");
        }
    }
}
