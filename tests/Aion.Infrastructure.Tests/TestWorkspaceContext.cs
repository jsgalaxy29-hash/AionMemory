using System;
using Aion.Domain;

namespace Aion.Infrastructure.Tests;

internal sealed class TestWorkspaceContext : IWorkspaceContext
{
    public Guid WorkspaceId { get; } = TenancyDefaults.DefaultWorkspaceId;
}
