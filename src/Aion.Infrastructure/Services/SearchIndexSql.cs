namespace Aion.Infrastructure.Services;

public static class SearchIndexSql
{
    public const string NoteSearch = """
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
""";

    public const string RecordSearch = """
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
""";

    public const string FileSearch = """
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
""";
}
