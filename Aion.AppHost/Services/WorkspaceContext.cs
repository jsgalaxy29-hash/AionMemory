using System;
using Aion.Domain;

namespace Aion.AppHost.Services;

public sealed class WorkspaceContext : IWorkspaceContextAccessor
{
    public Guid WorkspaceId { get; private set; } = TenancyDefaults.DefaultWorkspaceId;

    public void SetWorkspace(Guid workspaceId)
    {
        WorkspaceId = workspaceId == Guid.Empty ? TenancyDefaults.DefaultWorkspaceId : workspaceId;
    }
}
