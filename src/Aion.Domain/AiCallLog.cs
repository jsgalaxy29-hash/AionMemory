using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;

namespace Aion.Domain;

public sealed class AiCallLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    [Required, StringLength(64)]
    public string Provider { get; set; } = string.Empty;

    [StringLength(128)]
    public string? Model { get; set; }

    [Required, StringLength(32)]
    public string Operation { get; set; } = string.Empty;

    public long? Tokens { get; set; }

    public double? Cost { get; set; }

    public double DurationMs { get; set; }

    [Required, StringLength(32)]
    public string Status { get; set; } = AiCallStatus.Success;

    [StringLength(64)]
    public string? CorrelationId { get; set; }

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record AiCallLogEntry(
    string Provider,
    string? Model,
    string Operation,
    long? Tokens,
    double? Cost,
    double DurationMs,
    string Status);

public sealed record AiCallDiagnosticsQuery(DateTimeOffset? From = null, DateTimeOffset? To = null);

public sealed record AiCallDiagnostics(
    DateTimeOffset From,
    DateTimeOffset To,
    AiCallTotals Totals,
    IReadOnlyCollection<AiCallProviderStats> Providers);

public sealed record AiCallTotals(
    long TotalCalls,
    long TotalTokens,
    double TotalCost,
    double AverageDurationMs,
    long SuccessCalls,
    long ErrorCalls);

public sealed record AiCallProviderStats(
    string Provider,
    string? Model,
    long Calls,
    long Tokens,
    double Cost,
    double AverageDurationMs);

public interface IAiCallLogService
{
    Task LogAsync(AiCallLogEntry entry, CancellationToken cancellationToken = default);
    Task<AiCallDiagnostics> GetDiagnosticsAsync(AiCallDiagnosticsQuery query, CancellationToken cancellationToken = default);
}

public static class AiCallStatus
{
    public const string Success = "success";
    public const string Error = "error";
    public const string Fallback = "fallback";
    public const string Skipped = "skipped";
    public const string Inactive = "inactive";
}
