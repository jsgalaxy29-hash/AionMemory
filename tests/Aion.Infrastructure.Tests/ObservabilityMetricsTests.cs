using Aion.Infrastructure.Observability;
using Xunit;

namespace Aion.Infrastructure.Tests;

public sealed class ObservabilityMetricsTests
{
    [Fact]
    public void Infrastructure_metrics_are_noops_without_listeners()
    {
        InfrastructureMetrics.RecordDataEngineDuration("DataEngine.Query", TimeSpan.FromMilliseconds(5));
        InfrastructureMetrics.RecordDataEngineError("DataEngine.Query");
        InfrastructureMetrics.RecordSyncReplay();
        InfrastructureMetrics.RecordSyncConflict("plan");
    }
}
