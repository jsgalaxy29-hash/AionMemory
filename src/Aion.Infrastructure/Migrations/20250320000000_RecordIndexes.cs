using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RecordIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecordIndexes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityTypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FieldName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    StringValue = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    NumberValue = table.Column<decimal>(type: "TEXT", nullable: true),
                    DateValue = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    BoolValue = table.Column<bool>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordIndexes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecordIndexes_Records_RecordId",
                        column: x => x.RecordId,
                        principalTable: "Records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecordIndexes_EntityTypeId_FieldName_BoolValue",
                table: "RecordIndexes",
                columns: new[] { "EntityTypeId", "FieldName", "BoolValue" });

            migrationBuilder.CreateIndex(
                name: "IX_RecordIndexes_EntityTypeId_FieldName_DateValue",
                table: "RecordIndexes",
                columns: new[] { "EntityTypeId", "FieldName", "DateValue" });

            migrationBuilder.CreateIndex(
                name: "IX_RecordIndexes_EntityTypeId_FieldName_NumberValue",
                table: "RecordIndexes",
                columns: new[] { "EntityTypeId", "FieldName", "NumberValue" });

            migrationBuilder.CreateIndex(
                name: "IX_RecordIndexes_EntityTypeId_FieldName_RecordId",
                table: "RecordIndexes",
                columns: new[] { "EntityTypeId", "FieldName", "RecordId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecordIndexes_EntityTypeId_FieldName_StringValue",
                table: "RecordIndexes",
                columns: new[] { "EntityTypeId", "FieldName", "StringValue" });

            migrationBuilder.CreateIndex(
                name: "IX_RecordIndexes_RecordId",
                table: "RecordIndexes",
                column: "RecordId");

            migrationBuilder.Sql("""
INSERT INTO RecordIndexes (Id, EntityTypeId, RecordId, FieldName, StringValue, NumberValue, DateValue, BoolValue)
SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(6))),
       r.EntityTypeId,
       r.Id,
       f.Name,
       CASE WHEN f.DataType IN ('String','Note','Tags','Json','File','Relation','Enum') THEN json_extract(r.DataJson, '$.' || f.Name) END,
       CASE WHEN f.DataType IN ('Int','Decimal') THEN json_extract(r.DataJson, '$.' || f.Name) END,
       CASE WHEN f.DataType IN ('Date','DateTime') THEN json_extract(r.DataJson, '$.' || f.Name) END,
       CASE WHEN f.DataType = 'Bool' THEN json_extract(r.DataJson, '$.' || f.Name) END
FROM Records r
JOIN TableFields f ON f.EntityTypeId = r.EntityTypeId
WHERE json_extract(r.DataJson, '$.' || f.Name) IS NOT NULL;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecordIndexes");
        }
    }
}
