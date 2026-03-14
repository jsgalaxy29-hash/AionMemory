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
        cut.WaitForAssertion(() => Assert.Contains("Création", cut.Markup));

        cut.Find("[data-field-renderer='text'] input").Change("Bob");
        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Sélectionnez un enregistrement", cut.Markup);
            Assert.DoesNotContain("Création", cut.Markup);
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
        cut.WaitForAssertion(() => Assert.Contains("Édition", cut.Markup));

        var deleteButtons = cut.FindAll("button.button.danger");
        deleteButtons[^1].Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Sélectionnez un enregistrement", cut.Markup);
            Assert.DoesNotContain("Édition", cut.Markup);
        });
    }

    private void RegisterRuntimeServices(STable table, FakeRecordQueryService query)
    {
        Services.AddSingleton<IModuleViewService>(new FakeModuleViewService(new[] { table }));
        Services.AddSingleton<IRecordQueryService>(query);
        Services.AddSingleton<IDynamicFormRenderer, DynamicFormRenderer>();
        Services.AddSingleton<IDynamicListRenderer, DynamicListRenderer>();
        Services.AddSingleton<IDynamicFieldComponentRegistry, DynamicFieldComponentRegistry>();
        Services.AddSingleton<IDynamicListCellComponentRegistry, DynamicListCellComponentRegistry>();
    }

    private static STable BuildTable(Guid id, string displayName)
        => new()
        {
            Id = id,
            Name = "Contact",
            DisplayName = displayName,
            Fields =
            [
                new SFieldDefinition
                {
                    Id = Guid.NewGuid(),
                    Name = "Name",
                    Label = "Nom",
                    DataType = FieldDataType.Text,
                    IsRequired = true,
                    IsListVisible = true,
                    IsSortable = true
                }
            ]
        };
}
