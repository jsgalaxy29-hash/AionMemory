using System.ComponentModel.DataAnnotations.Schema;

namespace Aion.Infrastructure;

public sealed class NoteSearchEntry
{
    public Guid NoteId { get; set; }
    public string Content { get; set; } = string.Empty;
}

public sealed class RecordSearchEntry
{
    public Guid RecordId { get; set; }
    [Column("EntityTypeId")]
    public Guid TableId { get; set; }
    public string Content { get; set; } = string.Empty;
}

public sealed class FileSearchEntry
{
    public Guid FileId { get; set; }
    public string Content { get; set; } = string.Empty;
}

public sealed class SemanticSearchEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? EmbeddingJson { get; set; }
    public DateTimeOffset IndexedAt { get; set; } = DateTimeOffset.UtcNow;
}
