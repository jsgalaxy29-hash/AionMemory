using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SearchAugmentations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS FileSearch_ai;
DROP TRIGGER IF EXISTS FileSearch_au;
DROP TRIGGER IF EXISTS FileSearch_ad;
CREATE VIRTUAL TABLE IF NOT EXISTS FileSearch USING fts5(FileId UNINDEXED, Content);
CREATE TRIGGER FileSearch_ai AFTER INSERT ON Files BEGIN
    DELETE FROM FileSearch WHERE FileId = new.Id;
    INSERT INTO FileSearch(FileId, Content) VALUES (new.Id, COALESCE(new.FileName,'') || ' ' || COALESCE(new.MimeType,'') || ' ' || COALESCE(new.StoragePath,''));
END;
CREATE TRIGGER FileSearch_au AFTER UPDATE ON Files BEGIN
    DELETE FROM FileSearch WHERE FileId = new.Id;
    INSERT INTO FileSearch(FileId, Content) VALUES (new.Id, COALESCE(new.FileName,'') || ' ' || COALESCE(new.MimeType,'') || ' ' || COALESCE(new.StoragePath,''));
END;
CREATE TRIGGER FileSearch_ad AFTER DELETE ON Files BEGIN
    DELETE FROM FileSearch WHERE FileId = old.Id;
END;
INSERT INTO FileSearch(FileId, Content)
SELECT Id, COALESCE(FileName,'') || ' ' || COALESCE(MimeType,'') || ' ' || COALESCE(StoragePath,'') FROM Files;
");

            migrationBuilder.CreateTable(
                name: "SemanticSearchEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    EmbeddingJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: true),
                    IndexedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemanticSearchEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticSearchEntries_IndexedAt",
                table: "SemanticSearchEntries",
                column: "IndexedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SemanticSearchEntries_TargetType_TargetId",
                table: "SemanticSearchEntries",
                columns: new[] { "TargetType", "TargetId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SemanticSearchEntries");

            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS FileSearch_ai;
DROP TRIGGER IF EXISTS FileSearch_au;
DROP TRIGGER IF EXISTS FileSearch_ad;
DROP TABLE IF EXISTS FileSearch;
");
        }
    }
}
