using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Aion.Domain;

public enum KnowledgeRelationType
{
    LinkedTo,
    DependsOn,
    RelatedTo
}

public sealed class KnowledgeNode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TableId { get; set; }

    public Guid RecordId { get; set; }

    [Required, StringLength(256)]
    public string Title { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class KnowledgeEdge
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FromNodeId { get; set; }

    public Guid ToNodeId { get; set; }

    public KnowledgeRelationType RelationType { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record KnowledgeGraphSlice(KnowledgeNode Root, IReadOnlyCollection<KnowledgeNode> Nodes, IReadOnlyCollection<KnowledgeEdge> Edges);
