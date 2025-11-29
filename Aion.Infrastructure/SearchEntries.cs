namespace Aion.Infrastructure;

public sealed class NoteSearchEntry
{
    public Guid NoteId { get; set; }
    public string Content { get; set; } = string.Empty;
}

public sealed class RecordSearchEntry
{
    public Guid RecordId { get; set; }
    public Guid EntityTypeId { get; set; }
    public string Content { get; set; } = string.Empty;
}
