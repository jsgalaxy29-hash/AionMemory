using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aion.Infrastructure.Services;

public sealed class AuthorizationService : IAuthorizationService
{
    private readonly AionDbContext _dbContext;
    private readonly ILogger<AuthorizationService> _logger;
    private readonly ISecurityAuditService _securityAuditService;

    public AuthorizationService(AionDbContext dbContext, ILogger<AuthorizationService> logger, ISecurityAuditService securityAuditService)
    {
        _dbContext = dbContext;
        _logger = logger;
        _securityAuditService = securityAuditService;
    }

    public async Task<AuthorizationResult> AuthorizeAsync(Guid userId, PermissionAction action, PermissionScope scope, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return AuthorizationResult.Deny("UserId is required for authorization.");
        }

        ArgumentNullException.ThrowIfNull(scope);
        scope.Validate();

        var roles = await _dbContext.Roles
            .Where(r => r.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (roles.Any(r => r.Kind == RoleKind.Admin))
        {
            var adminResult = AuthorizationResult.Allow("Admin role grants full access.");
            await LogAccessAsync(userId, action, scope, adminResult, cancellationToken).ConfigureAwait(false);
            return adminResult;
        }

        if (!Enum.IsDefined(typeof(PermissionAction), action))
        {
            var unknownActionResult = AuthorizationResult.Deny("Unknown permission action.");
            await LogAccessAsync(userId, action, scope, unknownActionResult, cancellationToken).ConfigureAwait(false);
            return unknownActionResult;
        }

        var permissions = await _dbContext.Permissions
            .AsNoTracking()
            .Include(p => p.Scope)
            .Where(p => p.UserId == userId && p.Action == action)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (HasMatchingPermission(permissions, scope))
        {
            var allowResult = AuthorizationResult.Allow();
            await LogAccessAsync(userId, action, scope, allowResult, cancellationToken).ConfigureAwait(false);
            return allowResult;
        }

        _logger.LogWarning(
            "Authorization denied for user {UserId} on table {TableId} record {RecordId} field {FieldName} for action {Action}",
            userId,
            scope.TableId,
            scope.RecordId,
            scope.FieldName,
            action);
        var denyResult = AuthorizationResult.Deny("Access denied for the requested operation.");
        await LogAccessAsync(userId, action, scope, denyResult, cancellationToken).ConfigureAwait(false);
        return denyResult;
    }

    private static bool HasMatchingPermission(IEnumerable<Permission> permissions, PermissionScope scope)
    {
        return permissions.Any(p =>
            p.Scope is not null &&
            p.Scope.TableId == scope.TableId &&
            MatchesRecordScope(p.Scope, scope) &&
            MatchesFieldScope(p.Scope, scope));
    }

    private static bool MatchesRecordScope(PermissionScope permissionScope, PermissionScope requestedScope)
    {
        if (permissionScope.RecordId.HasValue)
        {
            return requestedScope.RecordId.HasValue && permissionScope.RecordId == requestedScope.RecordId;
        }

        return true;
    }

    private static bool MatchesFieldScope(PermissionScope permissionScope, PermissionScope requestedScope)
    {
        if (!string.IsNullOrWhiteSpace(permissionScope.FieldName))
        {
            return !string.IsNullOrWhiteSpace(requestedScope.FieldName)
                   && string.Equals(permissionScope.FieldName, requestedScope.FieldName, StringComparison.OrdinalIgnoreCase)
                   && (!permissionScope.RecordId.HasValue || requestedScope.RecordId == permissionScope.RecordId);
        }

        if (!string.IsNullOrWhiteSpace(requestedScope.FieldName))
        {
            return permissionScope.RecordId.HasValue
                   ? requestedScope.RecordId == permissionScope.RecordId
                   : true;
        }

        return true;
    }

    private async Task LogAccessAsync(Guid userId, PermissionAction action, PermissionScope scope, AuthorizationResult result, CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["AuthorizedUserId"] = userId,
            ["PermissionAction"] = action.ToString(),
            ["ScopeKind"] = scope.Kind.ToString(),
            ["TableId"] = scope.TableId,
            ["RecordId"] = scope.RecordId,
            ["FieldName"] = scope.FieldName,
            ["Allowed"] = result.IsAllowed,
            ["Reason"] = result.Reason
        };

        var auditEvent = new SecurityAuditEvent(
            SecurityAuditCategory.Authorization,
            result.IsAllowed ? "AccessAllowed" : "AccessDenied",
            scope.Kind.ToString(),
            scope.RecordId ?? scope.TableId,
            metadata);

        await _securityAuditService.LogAsync(auditEvent, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class CurrentUserService : ICurrentUserService
{
    public Guid GetCurrentUserId() => AuthorizationDefaults.DefaultUserId;
}
