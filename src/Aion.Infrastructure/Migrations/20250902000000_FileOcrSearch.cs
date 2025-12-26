using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FileOcrSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OcrText",
                table: "Files",
                type: "TEXT",
                maxLength: 16000,
                nullable: true);

            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS FileSearch_ai;
DROP TRIGGER IF EXISTS FileSearch_au;
DROP TRIGGER IF EXISTS FileSearch_ad;
CREATE VIRTUAL TABLE IF NOT EXISTS FileSearch USING fts5(FileId UNINDEXED, Content);
CREATE TRIGGER FileSearch_ai AFTER INSERT ON Files BEGIN
    DELETE FROM FileSearch WHERE FileId = new.Id;
    INSERT INTO FileSearch(FileId, Content) VALUES (new.Id, COALESCE(new.FileName,'') || ' ' || COALESCE(new.MimeType,'') || ' ' || COALESCE(new.StoragePath,'') || ' ' || COALESCE(new.OcrText,''));
END;
CREATE TRIGGER FileSearch_au AFTER UPDATE ON Files BEGIN
    DELETE FROM FileSearch WHERE FileId = new.Id;
    INSERT INTO FileSearch(FileId, Content) VALUES (new.Id, COALESCE(new.FileName,'') || ' ' || COALESCE(new.MimeType,'') || ' ' || COALESCE(new.StoragePath,'') || ' ' || COALESCE(new.OcrText,''));
END;
CREATE TRIGGER FileSearch_ad AFTER DELETE ON Files BEGIN
    DELETE FROM FileSearch WHERE FileId = old.Id;
END;
INSERT INTO FileSearch(FileId, Content)
SELECT Id, COALESCE(FileName,'') || ' ' || COALESCE(MimeType,'') || ' ' || COALESCE(StoragePath,'') || ' ' || COALESCE(OcrText,'') FROM Files;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            migrationBuilder.DropColumn(
                name: "OcrText",
                table: "Files");
        }
    }
}
