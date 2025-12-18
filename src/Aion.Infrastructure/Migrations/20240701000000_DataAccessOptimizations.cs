using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DataAccessOptimizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS NoteSearch_ai;
DROP TRIGGER IF EXISTS NoteSearch_au;
DROP TRIGGER IF EXISTS NoteSearch_ad;
CREATE VIRTUAL TABLE IF NOT EXISTS NoteSearch USING fts5(NoteId UNINDEXED, Content);
CREATE TRIGGER NoteSearch_ai AFTER INSERT ON Notes BEGIN
    DELETE FROM NoteSearch WHERE NoteId = new.Id;
    INSERT INTO NoteSearch(NoteId, Content) VALUES (new.Id, COALESCE(new.Title,'') || ' ' || COALESCE(new.Content,''));
END;
CREATE TRIGGER NoteSearch_au AFTER UPDATE ON Notes BEGIN
    DELETE FROM NoteSearch WHERE NoteId = new.Id;
    INSERT INTO NoteSearch(NoteId, Content) VALUES (new.Id, COALESCE(new.Title,'') || ' ' || COALESCE(new.Content,''));
END;
CREATE TRIGGER NoteSearch_ad AFTER DELETE ON Notes BEGIN
    DELETE FROM NoteSearch WHERE NoteId = old.Id;
END;
INSERT INTO NoteSearch(NoteId, Content)
SELECT Id, COALESCE(Title,'') || ' ' || COALESCE(Content,'') FROM Notes;
");

            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS RecordSearch_ai;
DROP TRIGGER IF EXISTS RecordSearch_au;
DROP TRIGGER IF EXISTS RecordSearch_ad;
CREATE VIRTUAL TABLE IF NOT EXISTS RecordSearch USING fts5(RecordId UNINDEXED, EntityTypeId UNINDEXED, Content);
CREATE TRIGGER RecordSearch_ai AFTER INSERT ON Records BEGIN
    DELETE FROM RecordSearch WHERE RecordId = new.Id;
    INSERT INTO RecordSearch(RecordId, EntityTypeId, Content) VALUES (new.Id, new.EntityTypeId, COALESCE(new.DataJson,''));
END;
CREATE TRIGGER RecordSearch_au AFTER UPDATE ON Records BEGIN
    DELETE FROM RecordSearch WHERE RecordId = new.Id;
    INSERT INTO RecordSearch(RecordId, EntityTypeId, Content) VALUES (new.Id, new.EntityTypeId, COALESCE(new.DataJson,''));
END;
CREATE TRIGGER RecordSearch_ad AFTER DELETE ON Records BEGIN
    DELETE FROM RecordSearch WHERE RecordId = old.Id;
END;
INSERT INTO RecordSearch(RecordId, EntityTypeId, Content)
SELECT Id, EntityTypeId, COALESCE(DataJson,'') FROM Records;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TABLE IF EXISTS NoteSearch;
DROP TABLE IF EXISTS RecordSearch;
");
        }
    }
}
