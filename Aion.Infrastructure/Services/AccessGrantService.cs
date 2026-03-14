using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;
using Microsoft.EntityFrameworkCore;

namespace Aion.Infrastructure.Services;

public sealed class AccessGrantService : IAccessGrantService
{
    private readonly AionDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly ISecurityAuditService _securityAuditService;

    public AccessGrantService(
        AionDbContext db,
        ICurrentUserService currentUserService,
        ISecurityAuditService securityAuditService)
    {
        _db = db;
        _currentUserService = currentUserService;
        _securityAuditService = securityAuditService;
    }

    public Task<Permission> GrantTableAsync(Guid targetUserId, PermissionAction action, Guid tableId, CancellationToken cancellationToken = default)
        => GrantSingleAsync(
            targetUserId,
            action,
            PermissionScope.ForTable(tableId),
            "Table",
            tableId,
            cancellationToken);

    public Task<Permission> GrantRecordAsync(Guid targetUserId, PermissionAction action, Guid tableId, Guid recordId, CancellationToken cancellationToken = default)
        => GrantSingleAsync(
            targetUserId,
            action,
            PermissionScope.ForRecord(tableId, recordId),
            "Record",
            recordId,
            cancellationToken);

    public Task<Permission> GrantFieldAsync(Guid targetUserId, PermissionAction action, Guid tableId, string fieldName, CancellationToken cancellationToken = default)
        => GrantSingleAsync(
            targetUserId,
            action,
            PermissionScope.ForField(tableId, fieldName),
            "Field",
            tableId,
            cancellationToken);

    public async Task<IReadOnlyList<Permission>> GrantModuleAsync(Guid targetUserId, PermissionAction action, Guid moduleId, CancellationToken cancellationToken = default)
    {
        ValidateInputs(targetUserId, action);

        var module = await _db.Modules
            .Include(m => m.EntityTypes)
            .FirstOrDefaultAsync(m => m.Id == moduleId, cancellationToken)
            .ConfigureAwait(false);

        if (module is null)
        {
            throw new InvalidOperationException($"Module {moduleId} not found.");
        }

        var tableIds = module.EntityTypes
            .Select(entity => entity.Id)
            .Distinct()
            .ToList();

        var permissions = await GrantMultipleAsync(
            targetUserId,
            action,
            tableIds.Select(PermissionScope.ForTable),
            "Module",
            moduleId,
            cancellationToken).ConfigureAwait(false);

        return permissions;
    }

    private async Task<Permission> GrantSingleAsync(
        Guid targetUserId,
        PermissionAction action,
        PermissionScope scope,
        string targetType,
        Guid? targetId,
        CancellationToken cancellationToken)
    {
        ValidateInputs(targetUserId, action);

        var existing = await FindExistingPermissionAsync(targetUserId, action, scope, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            await LogGrantAsync(existing, targetType, targetId, alreadyGranted: true).ConfigureAwait(false);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return existing;
        }

        var permission = CreatePermission(targetUserId, action, scope);
        await _db.Permissions.AddAsync(permission, cancellationToken).ConfigureAwait(false);
        await LogGrantAsync(permission, targetType, targetId, alreadyGranted: false).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return permission;
    }

    private async Task<IReadOnlyList<Permission>> GrantMultipleAsync(
        Guid targetUserId,
        PermissionAction action,
        IEnumerable<PermissionScope> scopes,
        string targetType,
        Guid? targetId,
        CancellationToken cancellationToken)
    {
        var scopeList = scopes.ToList();
        if (scopeList.Count == 0)
        {
            return Array.Empty<Permission>();
        }

        var existing = await _db.Permissions
            .AsNoTracking()
            .Include(p => p.Scope)
            .Where(p => p.UserId == targetUserId && p.Action == action)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var toCreate = new List<Permission>();
        foreach (var scope in scopeList)
        {
            var match = existing.FirstOrDefault(p => ScopeEquals(p.Scope, scope));
            if (match is null)
            {
                toCreate.Add(CreatePermission(targetUserId, action, scope));
            }
        }

        if (toCreate.Count > 0)
        {
            await _db.Permissions.AddRangeAsync(toCreate, cancellationToken).ConfigureAwait(false);
        }

        var metadata = new Dictionary<string, object?>
        {
            ["TargetUserId"] = targetUserId,
            ["PermissionAction"] = action.ToString(),
            ["ScopeCount"] = scopeList.Count,
            ["GrantedCount"] = toCreate.Count
        };

        _securityAuditService.Track(new SecurityAuditEvent(
            SecurityAuditCategory.Authorization,
            "PermissionGranted",
            targetType,
            targetId,
            metadata));

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return existing.Concat(toCreate).ToList();
    }

    private async Task<Permission?> FindExistingPermissionAsync(
        Guid targetUserId,
        PermissionAction action,
        PermissionScope scope,
        CancellationToken cancellationToken)
    {
        return await _db.Permissions
            .AsNoTracking()
            .Include(p => p.Scope)
            .FirstOrDefaultAsync(
                p => p.UserId == targetUserId
                     && p.Action == action
                     && p.Scope.TableId == scope.TableId
                     && p.Scope.RecordId == scope.RecordId
                     && string.Equals(p.Scope.FieldName, scope.FieldName, StringComparison.OrdinalIgnoreCase),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private Permission CreatePermission(Guid targetUserId, PermissionAction action, PermissionScope scope)
        => Permission.Grant(targetUserId, action, scope, _currentUserService.GetCurrentUserId());

    private void ValidateInputs(Guid targetUserId, PermissionAction action)
    {
        if (targetUserId == Guid.Empty)
        {
            throw new InvalidOperationException("Target user is required for permission grants.");
        }

        if (!Enum.IsDefined(typeof(PermissionAction), action))
        {
            throw new InvalidOperationException("Unknown permission action.");
        }
    }

    private Task LogGrantAsync(Permission permission, string targetType, Guid? targetId, bool alreadyGranted)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["TargetUserId"] = permission.UserId,
            ["PermissionAction"] = permission.Action.ToString(),
            ["TableId"] = permission.Scope.TableId,
            ["RecordId"] = permission.Scope.RecordId,
            ["FieldName"] = permission.Scope.FieldName,
            ["AlreadyGranted"] = alreadyGranted
        };

        _securityAuditService.Track(new SecurityAuditEvent(
            SecurityAuditCategory.Authorization,
            "PermissionGranted",
            targetType,
            targetId ?? permission.Id,
            metadata));
        return Task.CompletedTask;
    }

    private static bool ScopeEquals(PermissionScope left, PermissionScope right)
        => left.TableId == right.TableId
           && left.RecordId == right.RecordId
           && string.Equals(left.FieldName, right.FieldName, StringComparison.OrdinalIgnoreCase);
}
