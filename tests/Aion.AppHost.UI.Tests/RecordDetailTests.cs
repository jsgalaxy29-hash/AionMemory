using Aion.AI;
using Aion.AppHost.Components.Pages;
using Aion.AppHost.Services;
using Aion.Domain;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aion.AppHost.UI.Tests;

public class RecordDetailTests : TestContext
{
    [Fact]
    public void Record_detail_loads_record_and_events()
    {
        var tableId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var entity = new S_EntityType
        {
            Id = tableId,
            Name = "Contact",
            PluralName = "Contacts"
        };
        var module = new S_Module
        {
            Name = "CRM",
            EntityTypes = new List<S_EntityType> { entity }
        };
        var table = new STable
        {
            Id = tableId,
            Name = entity.Name,
            DisplayName = entity.PluralName
        };
        var resolved = new ResolvedRecord(
            new F_Record { Id = recordId, TableId = tableId, DataJson = "{}" },
            new Dictionary<string, object?> { ["Nom"] = "Alice" },
            new Dictionary<string, LookupResolution>());

        var events = new List<S_Event>
        {
            new()
            {
                Title = "Relance",
                Start = DateTimeOffset.Now.AddDays(1),
                Links = new List<J_Event_Link>
                {
                    new()
                    {
                        TargetType = entity.Name,
                        TargetId = recordId
                    }
                }
            }
        };

        Services.AddSingleton<IMetadataService>(new FakeMetadataService(new[] { module }));
        Services.AddSingleton<ITableDefinitionService>(new FakeTableDefinitionService(new[] { table }));
        Services.AddSingleton<IRecordQueryService>(new FakeRecordQueryService(resolvedRecords: new Dictionary<Guid, ResolvedRecord?> { [recordId] = resolved }));
        Services.AddSingleton<IModuleViewService>(new FakeModuleViewService(new[] { table }));
        Services.AddSingleton<INoteService>(new FakeNoteService());
        Services.AddSingleton<IAgendaService>(new FakeAgendaService(events));
        Services.AddSingleton<IFileStorageService>(new FakeFileStorageService());
        Services.AddSingleton<IAionVisionService>(new FakeVisionService());

        var cut = RenderComponent<RecordDetail>(parameters => parameters
            .Add(p => p.TableId, tableId.ToString())
            .Add(p => p.RecordId, recordId.ToString()));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Nom", cut.Markup);
            Assert.Contains("Alice", cut.Markup);
            Assert.Contains("Relance", cut.Markup);
        });
    }
}
