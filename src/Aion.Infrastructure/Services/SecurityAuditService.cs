using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Aion.Domain;

namespace Aion.Infrastructure.Services;

public sealed class SecurityAuditService : ISecurityAuditService
{
    private const int MetadataMaxLength = 4000;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] RedactedKeys =
    [
        "password",
        "secret",
        "token",
        "key",
        "apikey",
        "authorization",
        "credential"
    ];

    private readonly AionDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly IWorkspaceContext _workspaceContext;

    public SecurityAuditService(AionDbContext db, ICurrentUserService currentUserService, IWorkspaceContext workspaceContext)
    {
        _db = db;
        _currentUserService = currentUserService;
        _workspaceContext = workspaceContext;
    }

    public void Track(SecurityAuditEvent auditEvent)
        => _db.SecurityAuditLogs.Add(BuildEntry(auditEvent));

    public async Task LogAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        await _db.SecurityAuditLogs.AddAsync(BuildEntry(auditEvent), cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private S_SecurityAuditLog BuildEntry(SecurityAuditEvent auditEvent)
    {
        var context = OperationContext.Current;
        return new S_SecurityAuditLog
        {
            WorkspaceId = _workspaceContext.WorkspaceId,
            UserId = _currentUserService.GetCurrentUserId(),
            Category = auditEvent.Category,
            Action = auditEvent.Action,
            TargetType = auditEvent.TargetType,
            TargetId = auditEvent.TargetId,
            MetadataJson = SerializeMetadata(auditEvent.Metadata),
            CorrelationId = context.CorrelationId,
            OperationId = context.OperationId,
            OccurredAt = DateTimeOffset.UtcNow
        };
    }

    private static string? SerializeMetadata(IReadOnlyDictionary<string, object?>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        var sanitized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in metadata)
        {
            if (ShouldRedact(key))
            {
                sanitized[key] = "***";
                continue;
            }

            sanitized[key] = value is string text && text.Length > 256
                ? string.Concat(text.AsSpan(0, 256), "…")
                : value;
        }

        var json = JsonSerializer.Serialize(sanitized, SerializerOptions);
        if (json.Length <= MetadataMaxLength)
        {
            return json;
        }

        var truncated = new Dictionary<string, object?>
        {
            ["truncated"] = true,
            ["keys"] = sanitized.Keys
        };
        return JsonSerializer.Serialize(truncated, SerializerOptions);
    }

    private static bool ShouldRedact(string key)
        => RedactedKeys.Any(token => key.Contains(token, StringComparison.OrdinalIgnoreCase));
}
