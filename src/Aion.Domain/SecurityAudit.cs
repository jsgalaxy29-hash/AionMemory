using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Aion.Domain;

public enum SecurityAuditCategory
{
    SchemaChange,
    DataExport,
    DataImport,
    Backup,
    Restore,
    ModuleExport,
    ModuleImport
}

public sealed record SecurityAuditEvent(
    SecurityAuditCategory Category,
    string Action,
    string? TargetType = null,
    Guid? TargetId = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public interface ISecurityAuditService
{
    void Track(SecurityAuditEvent auditEvent);
    Task LogAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default);
}
