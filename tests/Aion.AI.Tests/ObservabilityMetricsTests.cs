using Aion.AI.Observability;
using Xunit;

namespace Aion.AI.Tests;

public sealed class ObservabilityMetricsTests
{
    [Fact]
    public void Ai_metrics_are_noops_without_listeners()
    {
        AiMetrics.RecordLatency("chat", "TestProvider", "model", TimeSpan.FromMilliseconds(12));
        AiMetrics.RecordError("chat", "TestProvider", "model");
        AiMetrics.RecordRetry("chat", "TestProvider");
        AiMetrics.RecordUsageFromJson("{\"usage\":{\"total_tokens\":42}}", "chat", "TestProvider", "model");
    }
}
