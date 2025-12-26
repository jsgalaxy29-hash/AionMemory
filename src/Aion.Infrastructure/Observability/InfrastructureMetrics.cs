using System.Diagnostics.Metrics;

namespace Aion.Infrastructure.Observability;

internal static class InfrastructureMetrics
{
    private static readonly Meter Meter = new("AionMemory.Infrastructure");
    private static readonly Histogram<double> DataEngineDurationMs = Meter.CreateHistogram<double>(
        "aion.dataengine.duration_ms",
        unit: "ms",
        description: "Durations for DataEngine operations.");
    private static readonly Counter<long> DataEngineErrors = Meter.CreateCounter<long>(
        "aion.dataengine.errors",
        description: "Errors for DataEngine operations.");
    private static readonly Counter<long> SyncReplays = Meter.CreateCounter<long>(
        "aion.sync.replays",
        description: "Sync operations that were already applied on the remote side.");
    private static readonly Counter<long> SyncConflicts = Meter.CreateCounter<long>(
        "aion.sync.conflicts",
        description: "Sync conflicts detected during sync planning or application.");
    private static readonly Counter<long> LookupValidationQueries = Meter.CreateCounter<long>(
        "aion.dataengine.lookup_validation_queries",
        description: "Queries executed while validating lookup references.");
    private static readonly Counter<long> LookupValidationCacheHits = Meter.CreateCounter<long>(
        "aion.dataengine.lookup_validation_cache_hits",
        description: "Lookup references satisfied by the per-request cache.");

    internal static void RecordDataEngineDuration(string operationName, TimeSpan elapsed)
    {
        var tags = new TagList
        {
            { "operation", NormalizeDataEngineOperation(operationName) }
        };
        DataEngineDurationMs.Record(elapsed.TotalMilliseconds, tags);
    }

    internal static void RecordDataEngineError(string operationName)
    {
        var tags = new TagList
        {
            { "operation", NormalizeDataEngineOperation(operationName) }
        };
        DataEngineErrors.Add(1, tags);
    }

    internal static void RecordSyncReplay()
    {
        SyncReplays.Add(1);
    }

    internal static void RecordSyncConflict(string stage)
    {
        var tags = new TagList
        {
            { "stage", stage }
        };
        SyncConflicts.Add(1, tags);
    }

    internal static void RecordLookupValidationQueries(int count)
    {
        if (count <= 0)
        {
            return;
        }

        LookupValidationQueries.Add(count);
    }

    internal static void RecordLookupValidationCacheHits(int count)
    {
        if (count <= 0)
        {
            return;
        }

        LookupValidationCacheHits.Add(count);
    }

    private static string NormalizeDataEngineOperation(string operationName)
    {
        if (string.IsNullOrWhiteSpace(operationName))
        {
            return "unknown";
        }

        var normalized = operationName.Replace("DataEngine.", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (normalized.StartsWith("Create", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Insert", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Generate", StringComparison.OrdinalIgnoreCase))
        {
            return "Create";
        }

        if (normalized.StartsWith("Update", StringComparison.OrdinalIgnoreCase))
        {
            return "Update";
        }

        if (normalized.StartsWith("Query", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Get", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Count", StringComparison.OrdinalIgnoreCase))
        {
            return "Query";
        }

        if (normalized.StartsWith("Search", StringComparison.OrdinalIgnoreCase))
        {
            return "Search";
        }

        return normalized;
    }
}
