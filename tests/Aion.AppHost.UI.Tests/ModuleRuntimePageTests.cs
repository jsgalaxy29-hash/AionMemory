using System.Text.Json;
using Aion.AppHost.Components.Pages;
using Aion.AppHost.Services;
using Aion.AppHost.Services.Rendering;
using Aion.Domain;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aion.AppHost.UI.Tests;

public sealed class ModuleRuntimePageTests : TestContext
{
    [Fact]
    public void Runtime_page_loads_metadata_and_displays_list()
    {
        var tableId = Guid.NewGuid();
        var table = BuildTable(tableId, "Contacts");
        var recordId = Guid.NewGuid();
        var query = new FakeRecordQueryService(recordsByTable: new Dictionary<Guid, List<F_Record>>
        {
            [tableId] =
            [
                new F_Record
                {
                    Id = recordId,
                    TableId = tableId,
                    DataJson = JsonSerializer.Serialize(new Dictionary<string, object?> { ["Name"] = "Alice" }),
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ]
        });

        RegisterRuntimeServices(table, query);

        var cut = RenderComponent<ModuleRuntimePage>(parameters => parameters
            .Add(p => p.TableId, tableId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Contacts", cut.Markup);
            Assert.Contains("Alice", cut.Markup);
        });
    }

    [Fact]
    public void Runtime_page_opens_create_form_and_returns_to_list_after_save()
    {
        var tableId = Guid.NewGuid();
        var table = BuildTable(tableId, "Contacts");
        var query = new FakeRecordQueryService(recordsByTable: new Dictionary<Guid, List<F_Record>>
        {
            [tableId] = []
        });

        RegisterRuntimeServices(table, query);

        var cut = RenderComponent<ModuleRuntimePage>(parameters => parameters
            .Add(p => p.TableId, tableId));

        cut.Find("button.button").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-field-renderer='text']")));

        cut.Find("[data-field-renderer='text']").Change("Bob");
        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-field-renderer='text']"));
        });
    }

    [Fact]
    public void Runtime_page_opens_edit_and_returns_to_list_after_delete()
    {
        var tableId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var table = BuildTable(tableId, "Contacts");
        var query = new FakeRecordQueryService(recordsByTable: new Dictionary<Guid, List<F_Record>>
        {
            [tableId] =
            [
                new F_Record
                {
                    Id = recordId,
                    TableId = tableId,
                    DataJson = JsonSerializer.Serialize(new Dictionary<string, object?> { ["Name"] = "Alice" }),
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ]
        });

        RegisterRuntimeServices(table, query);

        var cut = RenderComponent<ModuleRuntimePage>(parameters => parameters
            .Add(p => p.TableId, tableId));

        cut.WaitForAssertion(() => Assert.Contains("Alice", cut.Markup));

        cut.Find("tbody tr").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-field-renderer='text']")));

        var deleteButtons = cut.FindAll("button.button.danger");
        deleteButtons[^1].Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-field-renderer='text']"));
        });
    }

    private void RegisterRuntimeServices(STable table, FakeRecordQueryService query)
    {
        Services.AddAppHostUiDefaults();
        Services.AddSingleton<IModuleViewService>(new FakeModuleViewService(new[] { table }));
        Services.AddSingleton<IRecordQueryService>(query);
    }

    private static STable BuildTable(Guid id, string displayName)
    {
        var table = new STable
        {
            Id = id,
            Name = "Contact",
            DisplayName = displayName
        };
        table.Fields.Add(new SFieldDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Name",
            Label = "Nom",
            DataType = FieldDataType.Text,
            IsRequired = true,
            IsListVisible = true,
            IsSortable = true
        });
        return table;
    }
}
