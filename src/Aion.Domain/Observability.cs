using System;
using System.Diagnostics;

namespace Aion.Domain;

public readonly record struct OperationContext(string CorrelationId, string OperationId)
{
    public static OperationContext Current => FromActivity(Activity.Current);

    public static OperationContext CreateUntracked()
    {
        var traceId = ActivityTraceId.CreateRandom().ToString();
        var spanId = ActivitySpanId.CreateRandom().ToString();
        return new OperationContext(traceId, spanId);
    }

    public static OperationContext FromActivity(Activity? activity)
    {
        activity ??= new Activity("operation");
        if (activity.Id is null)
        {
            activity.Start();
        }

        return new OperationContext(activity.TraceId.ToString(), activity.SpanId.ToString());
    }
}

public interface IOperationScope : IDisposable
{
    OperationContext Context { get; }
}

public interface IOperationScopeFactory
{
    IOperationScope Start(string operationName);
    IOperationScope Start(string operationName, OperationContext parentContext);
}

public sealed class NoopOperationScopeFactory : IOperationScopeFactory
{
    public IOperationScope Start(string operationName) => NoopOperationScope.Instance;

    public IOperationScope Start(string operationName, OperationContext parentContext) => NoopOperationScope.Instance;

    private sealed class NoopOperationScope : IOperationScope
    {
        public static readonly NoopOperationScope Instance = new();

        public OperationContext Context { get; } = OperationContext.CreateUntracked();

        public void Dispose()
        {
        }
    }
}
