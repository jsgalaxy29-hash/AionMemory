using Aion.Domain;

namespace Aion.AppHost.Services;

/// <summary>
/// Minimal scoped state container to track the current module/entity context for navigation and deep-linking.
/// </summary>
public sealed class UiState
{
    public S_Module? CurrentModule { get; private set; }
    public S_EntityType? CurrentEntity { get; private set; }

    public event Action? OnChange;

    public void SetModule(S_Module? module)
    {
        CurrentModule = module;
        Notify();
    }

    public void SetEntity(S_EntityType? entity)
    {
        CurrentEntity = entity;
        Notify();
    }

    private void Notify() => OnChange?.Invoke();
}
