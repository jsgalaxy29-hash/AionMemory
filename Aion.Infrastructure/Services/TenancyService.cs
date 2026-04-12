using System;
using System.Linq;
using Aion.Domain;
using Microsoft.EntityFrameworkCore;

namespace Aion.Infrastructure.Services;

public sealed class TenancyService : ITenancyService
{
    private readonly AionDbContext _db;

    public TenancyService(AionDbContext db)
    {
        _db = db;
    }

    public async Task EnsureDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == TenancyDefaults.DefaultTenantId, cancellationToken)
            .ConfigureAwait(false);
        if (tenant is null)
        {
            tenant = new Tenant
            {
                Id = TenancyDefaults.DefaultTenantId,
                Name = TenancyDefaults.DefaultTenantName,
                Kind = TenantKind.Solo
            };
            _db.Tenants.Add(tenant);
        }

        var workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == TenancyDefaults.DefaultWorkspaceId, cancellationToken)
            .ConfigureAwait(false);
        if (workspace is null)
        {
            workspace = new Workspace
            {
                Id = TenancyDefaults.DefaultWorkspaceId,
                TenantId = tenant.Id,
                Name = TenancyDefaults.DefaultWorkspaceName,
                IsDefault = true
            };
            _db.Workspaces.Add(workspace);
        }

        var profile = await _db.Profiles.FirstOrDefaultAsync(p => p.Id == TenancyDefaults.DefaultProfileId, cancellationToken)
            .ConfigureAwait(false);
        if (profile is null)
        {
            profile = new Profile
            {
                Id = TenancyDefaults.DefaultProfileId,
                WorkspaceId = workspace.Id,
                DisplayName = TenancyDefaults.DefaultProfileName,
                Initials = "ME"
            };
            _db.Profiles.Add(profile);
        }

        if (_db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<Tenant>> GetTenantsAsync(CancellationToken cancellationToken = default)
        => await _db.Tenants.AsNoTracking().OrderBy(t => t.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Workspace>> GetWorkspacesAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => await _db.Workspaces.AsNoTracking()
            .Where(w => w.TenantId == tenantId)
            .OrderByDescending(w => w.IsDefault)
            .ThenBy(w => w.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Profile>> GetProfilesAsync(Guid workspaceId, CancellationToken cancellationToken = default)
        => await _db.Profiles.AsNoTracking()
            .Where(p => p.WorkspaceId == workspaceId)
            .OrderBy(p => p.DisplayName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task UpdateDefaultProfileAsync(string displayName, string? initials, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }

        var profile = await _db.Profiles.FirstOrDefaultAsync(p => p.Id == TenancyDefaults.DefaultProfileId, cancellationToken)
            .ConfigureAwait(false);
        if (profile is null)
        {
            return;
        }

        var trimmedName = displayName.Trim();
        var trimmedInitials = string.IsNullOrWhiteSpace(initials) ? profile.Initials : initials.Trim();

        var hasChanges = false;
        if (!string.Equals(profile.DisplayName, trimmedName, StringComparison.Ordinal))
        {
            profile.DisplayName = trimmedName;
            hasChanges = true;
        }

        if (!string.IsNullOrWhiteSpace(trimmedInitials) && !string.Equals(profile.Initials, trimmedInitials, StringComparison.Ordinal))
        {
            profile.Initials = trimmedInitials;
            hasChanges = true;
        }

        if (hasChanges)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
