using System;
using Aion.Domain;

namespace Aion.Infrastructure;

internal sealed class DefaultWorkspaceContext : IWorkspaceContext
{
    public Guid WorkspaceId { get; } = TenancyDefaults.DefaultWorkspaceId;
}
