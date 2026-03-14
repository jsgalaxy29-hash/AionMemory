namespace Aion.Domain;

public sealed record LinkCreateRequest(
    Guid SourceId,
    Guid TargetId,
    string Type,
    string? Reason = null,
    string? SourceType = null,
    string? TargetType = null,
    Guid? CreatedBy = null);

public sealed record LinkGraphSlice(Guid RootId, IReadOnlyCollection<Guid> Nodes, IReadOnlyCollection<S_Link> Links);

public interface ILinkGraphService
{
    Task<S_Link> CreateLinkAsync(LinkCreateRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<S_Link>> GetNeighborsAsync(Guid recordId, string? type = null, CancellationToken cancellationToken = default);
    Task<LinkGraphSlice> TraverseAsync(Guid recordId, int depth = 2, string? type = null, CancellationToken cancellationToken = default);
}
