namespace Aion.Domain;

public interface ITenancyService
{
    Task EnsureDefaultsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Tenant>> GetTenantsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Workspace>> GetWorkspacesAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Profile>> GetProfilesAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}

public interface IWorkspaceContext
{
    Guid WorkspaceId { get; }
}

public interface IWorkspaceContextAccessor : IWorkspaceContext
{
    void SetWorkspace(Guid workspaceId);
}
