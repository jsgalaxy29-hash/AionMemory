using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;

namespace Aion.Infrastructure.Tests;

public sealed class NullSecurityAuditService : ISecurityAuditService
{
    public void Track(SecurityAuditEvent auditEvent)
    {
    }

    public Task LogAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
