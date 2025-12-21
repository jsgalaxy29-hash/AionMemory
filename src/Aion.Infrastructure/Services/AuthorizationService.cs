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

    public AuthorizationService(AionDbContext dbContext, ILogger<AuthorizationService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
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
            return AuthorizationResult.Allow("Admin role grants full access.");
        }

        if (!Enum.IsDefined(typeof(PermissionAction), action))
        {
            return AuthorizationResult.Deny("Unknown permission action.");
        }

        var permissions = await _dbContext.Permissions
            .AsNoTracking()
            .Include(p => p.Scope)
            .Where(p => p.UserId == userId && p.Action == action)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (HasMatchingPermission(permissions, scope))
        {
            return AuthorizationResult.Allow();
        }

        _logger.LogWarning("Authorization denied for user {UserId} on table {TableId} for action {Action}", userId, scope.TableId, action);
        return AuthorizationResult.Deny("Access denied for the requested operation.");
    }

    private static bool HasMatchingPermission(IEnumerable<Permission> permissions, PermissionScope scope)
    {
        return permissions.Any(p =>
            p.Scope is not null &&
            p.Scope.TableId == scope.TableId &&
            (!scope.RecordId.HasValue || p.Scope.RecordId == null || p.Scope.RecordId == scope.RecordId));
    }
}

public sealed class CurrentUserService : ICurrentUserService
{
    public Guid GetCurrentUserId() => AuthorizationDefaults.DefaultUserId;
}
