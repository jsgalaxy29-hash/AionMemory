using System;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;

namespace Aion.AI.Observability;

internal sealed class NoopAiCallLogService : IAiCallLogService
{
    public static readonly NoopAiCallLogService Instance = new();

    public Task LogAsync(AiCallLogEntry entry, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<AiCallDiagnostics> GetDiagnosticsAsync(AiCallDiagnosticsQuery query, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var diagnostics = new AiCallDiagnostics(
            now,
            now,
            new AiCallTotals(0, 0, 0, 0, 0, 0),
            Array.Empty<AiCallProviderStats>());
        return Task.FromResult(diagnostics);
    }
}
