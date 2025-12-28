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
    Record,
    Field
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

    private PermissionScope(Guid tableId, Guid? recordId, string? fieldName)
    {
        TableId = tableId;
        RecordId = recordId;
        FieldName = fieldName;
    }

    public Guid TableId { get; private set; }

    public Guid? RecordId { get; private set; }

    public string? FieldName { get; private set; }

    [NotMapped]
    public PermissionScopeKind Kind => RecordId.HasValue
        ? PermissionScopeKind.Record
        : string.IsNullOrWhiteSpace(FieldName)
            ? PermissionScopeKind.Table
            : PermissionScopeKind.Field;

    public static PermissionScope ForTable(Guid tableId)
    {
        var scope = new PermissionScope(tableId, null, null);
        scope.Validate();
        return scope;
    }

    public static PermissionScope ForRecord(Guid tableId, Guid recordId)
    {
        var scope = new PermissionScope(tableId, recordId, null);
        scope.Validate();
        return scope;
    }

    public static PermissionScope ForField(Guid tableId, string fieldName)
    {
        var scope = new PermissionScope(tableId, null, fieldName);
        scope.Validate();
        return scope;
    }

    public static PermissionScope ForRecordField(Guid tableId, Guid recordId, string fieldName)
    {
        var scope = new PermissionScope(tableId, recordId, fieldName);
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

        if (FieldName is not null && string.IsNullOrWhiteSpace(FieldName))
        {
            throw new InvalidOperationException("FieldName cannot be empty when provided.");
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

    public Guid? GrantedByUserId { get; set; }

    public DateTimeOffset? GrantedAt { get; set; }

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

        if (GrantedByUserId.HasValue && GrantedByUserId.Value == Guid.Empty)
        {
            throw new InvalidOperationException("GrantedByUserId cannot be empty when provided.");
        }

        ArgumentNullException.ThrowIfNull(Scope);
        Scope.Validate();
    }

    public static Permission Grant(Guid userId, PermissionAction action, PermissionScope scope, Guid? grantedByUserId = null)
    {
        var permission = new Permission
        {
            UserId = userId,
            Action = action,
            Scope = scope,
            GrantedByUserId = grantedByUserId,
            GrantedAt = grantedByUserId.HasValue ? DateTimeOffset.UtcNow : null
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
