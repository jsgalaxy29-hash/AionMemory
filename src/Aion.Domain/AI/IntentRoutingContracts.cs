namespace Aion.AI;

public sealed record IntentRouteRequest
{
    public required string Input { get; init; }
    public required IntentDetectionResult Intent { get; init; }
    public S_Module? ModuleContext { get; init; }
    public Guid? ModuleId { get; init; }
    public bool IsOffline { get; init; }
}

public sealed record IntentRouteResult(IntentClass IntentClass, string Response, bool UsedFallback);

public interface IIntentRouter
{
    Task<IntentRouteResult> RouteAsync(IntentRouteRequest request, CancellationToken cancellationToken = default);
}
