using System;
using Aion.Domain;
using Xunit;

namespace Aion.Domain.Tests;

public class AuthorizationTests
{
    [Fact]
    public void PermissionScope_ForTable_requires_table_id()
    {
        Assert.Throws<InvalidOperationException>(() => PermissionScope.ForTable(Guid.Empty));
    }

    [Fact]
    public void PermissionScope_ForRecord_requires_record_id()
    {
        var tableId = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => PermissionScope.ForRecord(tableId, Guid.Empty));
    }

    [Fact]
    public void Role_requires_user_id()
    {
        Assert.Throws<InvalidOperationException>(() => Role.Assign(Guid.Empty, RoleKind.Admin));
    }

    [Fact]
    public void Permission_requires_scope_and_action()
    {
        var scope = PermissionScope.ForTable(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => Permission.Grant(Guid.NewGuid(), (PermissionAction)999, scope));

        Assert.Throws<ArgumentNullException>(() => Permission.Grant(Guid.NewGuid(), PermissionAction.Read, null!));
    }
}
