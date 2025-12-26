using System;
using System.ComponentModel.DataAnnotations;

namespace Aion.Domain;

public sealed class S_SecurityAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid UserId { get; set; }

    public SecurityAuditCategory Category { get; set; }

    [Required, StringLength(128)]
    public string Action { get; set; } = string.Empty;

    [StringLength(128)]
    public string? TargetType { get; set; }

    public Guid? TargetId { get; set; }

    [StringLength(4000)]
    public string? MetadataJson { get; set; }

    [StringLength(64)]
    public string? CorrelationId { get; set; }

    [StringLength(64)]
    public string? OperationId { get; set; }

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
