using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aion.Domain;
using Microsoft.Maui.Storage;

namespace Aion.AppHost.Services;

public sealed class WorkspaceSelectionState
{
    private const string WorkspacePreferenceKey = "aion.workspace";
    private const string ProfilePreferenceKey = "aion.profile";

    private readonly ITenancyService _tenancyService;
    private readonly IWorkspaceContextAccessor _workspaceContext;

    public WorkspaceSelectionState(ITenancyService tenancyService, IWorkspaceContextAccessor workspaceContext)
    {
        _tenancyService = tenancyService;
        _workspaceContext = workspaceContext;
    }

    public IReadOnlyList<Tenant> Tenants { get; private set; } = Array.Empty<Tenant>();
    public IReadOnlyList<Workspace> Workspaces { get; private set; } = Array.Empty<Workspace>();
    public IReadOnlyList<Profile> Profiles { get; private set; } = Array.Empty<Profile>();

    public Tenant? CurrentTenant { get; private set; }
    public Workspace? CurrentWorkspace { get; private set; }
    public Profile? CurrentProfile { get; private set; }
    public bool IsInitialized { get; private set; }

    public event Action? OnChange;

    public async Task InitializeAsync()
    {
        if (IsInitialized)
        {
            return;
        }

        await _tenancyService.EnsureDefaultsAsync().ConfigureAwait(false);
        Tenants = await _tenancyService.GetTenantsAsync().ConfigureAwait(false);
        CurrentTenant = Tenants.FirstOrDefault();

        var preferredWorkspace = LoadGuidPreference(WorkspacePreferenceKey);
        var preferredProfile = LoadGuidPreference(ProfilePreferenceKey);

        if (CurrentTenant is not null)
        {
            Workspaces = await _tenancyService.GetWorkspacesAsync(CurrentTenant.Id).ConfigureAwait(false);
            CurrentWorkspace = Workspaces.FirstOrDefault(w => w.Id == preferredWorkspace) ?? Workspaces.FirstOrDefault();
            await SetWorkspaceInternalAsync(CurrentWorkspace, preferredProfile).ConfigureAwait(false);
        }

        IsInitialized = true;
    }

    public async Task SelectWorkspaceAsync(Guid workspaceId)
    {
        if (CurrentTenant is null)
        {
            return;
        }

        Workspaces = await _tenancyService.GetWorkspacesAsync(CurrentTenant.Id).ConfigureAwait(false);
        CurrentWorkspace = Workspaces.FirstOrDefault(w => w.Id == workspaceId) ?? Workspaces.FirstOrDefault();
        await SetWorkspaceInternalAsync(CurrentWorkspace, null).ConfigureAwait(false);
    }

    public async Task SelectProfileAsync(Guid profileId)
    {
        if (CurrentWorkspace is null)
        {
            return;
        }

        Profiles = await _tenancyService.GetProfilesAsync(CurrentWorkspace.Id).ConfigureAwait(false);
        CurrentProfile = Profiles.FirstOrDefault(p => p.Id == profileId) ?? Profiles.FirstOrDefault();
        SaveGuidPreference(ProfilePreferenceKey, CurrentProfile?.Id);
        OnChange?.Invoke();
    }

    private async Task SetWorkspaceInternalAsync(Workspace? workspace, Guid? preferredProfileId)
    {
        if (workspace is null)
        {
            return;
        }

        _workspaceContext.SetWorkspace(workspace.Id);
        SaveGuidPreference(WorkspacePreferenceKey, workspace.Id);

        Profiles = await _tenancyService.GetProfilesAsync(workspace.Id).ConfigureAwait(false);
        CurrentProfile = Profiles.FirstOrDefault(p => p.Id == preferredProfileId) ?? Profiles.FirstOrDefault();
        SaveGuidPreference(ProfilePreferenceKey, CurrentProfile?.Id);
        OnChange?.Invoke();
    }

    private static Guid? LoadGuidPreference(string key)
    {
        var raw = Preferences.Default.Get(key, string.Empty);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private static void SaveGuidPreference(string key, Guid? value)
    {
        if (value is null)
        {
            Preferences.Default.Remove(key);
            return;
        }

        Preferences.Default.Set(key, value.Value.ToString());
    }
}
