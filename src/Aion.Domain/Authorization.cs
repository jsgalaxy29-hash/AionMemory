using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aion.Domain;

public enum RoleKind
{
    Admin,
    User
}

public enum PermissionAction
{
    Read,
    Write,
    Delete,
    ManageSchema
}

public enum PermissionScopeKind
{
    Table,
    Record
}

public static class AuthorizationDefaults
{
    public static Guid DefaultUserId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public static Guid AdminUserId => DefaultUserId;
}

public sealed class PermissionScope
{
    public PermissionScope()
    {
    }

    private PermissionScope(Guid tableId, Guid? recordId)
    {
        TableId = tableId;
        RecordId = recordId;
    }

    public Guid TableId { get; private set; }

    public Guid? RecordId { get; private set; }

    [NotMapped]
    public PermissionScopeKind Kind => RecordId.HasValue ? PermissionScopeKind.Record : PermissionScopeKind.Table;

    public static PermissionScope ForTable(Guid tableId)
    {
        var scope = new PermissionScope(tableId, null);
        scope.Validate();
        return scope;
    }

    public static PermissionScope ForRecord(Guid tableId, Guid recordId)
    {
        var scope = new PermissionScope(tableId, recordId);
        scope.Validate();
        return scope;
    }

    public void Validate()
    {
        if (TableId == Guid.Empty)
        {
            throw new InvalidOperationException("A permission scope requires a table identifier.");
        }

        if (RecordId.HasValue && RecordId.Value == Guid.Empty)
        {
            throw new InvalidOperationException("RecordId cannot be empty when provided.");
        }
    }
}

public sealed class Role
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public RoleKind Kind { get; set; }

    public void Validate()
    {
        if (UserId == Guid.Empty)
        {
            throw new InvalidOperationException("UserId is required for a role assignment.");
        }

        if (!Enum.IsDefined(typeof(RoleKind), Kind))
        {
            throw new InvalidOperationException("An invalid role kind was provided.");
        }
    }

    public static Role Assign(Guid userId, RoleKind kind)
    {
        var role = new Role { UserId = userId, Kind = kind };
        role.Validate();
        return role;
    }
}

public sealed class Permission
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public PermissionAction Action { get; set; }

    public PermissionScope Scope { get; set; } = null!;

    public void Validate()
    {
        if (UserId == Guid.Empty)
        {
            throw new InvalidOperationException("UserId is required for a permission.");
        }

        if (!Enum.IsDefined(typeof(PermissionAction), Action))
        {
            throw new InvalidOperationException("An invalid permission action was provided.");
        }

        ArgumentNullException.ThrowIfNull(Scope);
        Scope.Validate();
    }

    public static Permission Grant(Guid userId, PermissionAction action, PermissionScope scope)
    {
        var permission = new Permission
        {
            UserId = userId,
            Action = action,
            Scope = scope
        };

        permission.Validate();
        return permission;
    }
}

public sealed record AuthorizationResult(bool IsAllowed, string? Reason = null)
{
    public static AuthorizationResult Allow(string? reason = null) => new(true, reason);

    public static AuthorizationResult Deny(string reason) => new(false, reason);
}
