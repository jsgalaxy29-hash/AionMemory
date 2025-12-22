using System.Diagnostics;
using Aion.Domain;

namespace Aion.Infrastructure.Observability;

public sealed class OperationScopeFactory : IOperationScopeFactory
{
    private static readonly ActivitySource ActivitySource = new("AionMemory");

    public IOperationScope Start(string operationName)
    {
        var activity = ActivitySource.StartActivity(operationName, ActivityKind.Internal) ?? CreateActivity(operationName);
        return new ActivityOperationScope(activity, OperationContext.FromActivity(activity));
    }

    public IOperationScope Start(string operationName, OperationContext parentContext)
    {
        var activity = ActivitySource.StartActivity(operationName, ActivityKind.Internal, BuildParentContext(parentContext))
            ?? CreateActivity(operationName);
        return new ActivityOperationScope(activity, OperationContext.FromActivity(activity));
    }

    private static ActivityContext BuildParentContext(OperationContext parentContext)
    {
        try
        {
            var traceId = ActivityTraceId.CreateFromString(parentContext.CorrelationId.AsSpan());
            var spanId = ActivitySpanId.CreateFromString(parentContext.OperationId.AsSpan());
            return new ActivityContext(traceId, spanId, ActivityTraceFlags.Recorded);
        }
        catch (FormatException)
        {
            return new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);
        }
    }

    private static Activity CreateActivity(string operationName)
    {
        var activity = new Activity(operationName);
        activity.Start();
        return activity;
    }

    private sealed class ActivityOperationScope : IOperationScope
    {
        private readonly Activity? _activity;

        public ActivityOperationScope(Activity? activity, OperationContext context)
        {
            _activity = activity;
            Context = context;
        }

        public OperationContext Context { get; }

        public void Dispose()
        {
            _activity?.Stop();
        }
    }
}
