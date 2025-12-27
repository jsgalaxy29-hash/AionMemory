using System;
using Aion.Domain;
using Aion.AppHost.Components.Pages;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Aion.AppHost.UI.Tests;

public class ModulesHomeTests : TestContext
{
    [Fact]
    public void Empty_state_displays_cta()
    {
        Services.AddSingleton<IMetadataService>(new FakeMetadataService());

        var cut = RenderComponent<ModulesHome>();

        cut.WaitForAssertion(() =>
            cut.Markup.Contains("Aucun module pour le moment."));
    }

    [Fact]
    public void Modules_are_rendered_with_entity_badges()
    {
        var modules = new[]
        {
            BuildModule("Potager", "Plantations", "Récoltes"),
            BuildModule("Projets", "Tâches")
        };
        Services.AddSingleton<IMetadataService>(new FakeMetadataService(modules));

        var cut = RenderComponent<ModulesHome>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Potager", cut.Markup);
            Assert.Contains("Projets", cut.Markup);
            Assert.Equal(2, cut.FindAll(".module-card").Count);
            Assert.Contains("2 entités", cut.Markup);
        });
    }

    [Fact]
    public void Plus_button_navigates_to_module_builder()
    {
        Services.AddSingleton<IMetadataService>(new FakeMetadataService());
        var navManager = Services.GetRequiredService<NavigationManager>() as TestNavigationManager;
        Assert.NotNull(navManager);

        var cut = RenderComponent<ModulesHome>();
        cut.Find("button[title=\"Créer un module\"]").Click();

        Assert.EndsWith("/module-builder", navManager!.Uri, StringComparison.Ordinal);
    }

    private static S_Module BuildModule(string name, params string[] entityNames)
    {
        return new S_Module
        {
            Name = name,
            Description = $"Module {name}",
            EntityTypes = entityNames
                .Select(entity => new S_EntityType { Name = entity, PluralName = entity })
                .ToList()
        };
    }

    private sealed class FakeMetadataService : IMetadataService
    {
        private readonly IReadOnlyList<S_Module> _modules;

        public FakeMetadataService(IEnumerable<S_Module>? modules = null)
        {
            _modules = modules?.ToList() ?? new List<S_Module>();
        }

        public Task<IEnumerable<S_Module>> GetModulesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<S_Module>>(_modules);

        public Task<S_Module> CreateModuleAsync(S_Module module, CancellationToken cancellationToken = default)
            => Task.FromResult(module);

        public Task<S_EntityType> AddEntityTypeAsync(Guid moduleId, S_EntityType entityType, CancellationToken cancellationToken = default)
            => Task.FromResult(entityType);
    }
}
