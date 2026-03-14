using System.Text.Json;
using Aion.AppHost.Services.Rendering;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace Aion.AppHost.UI.Tests;

public sealed class DynamicListCellRegistryTests : TestContext
{
    private static readonly DynamicListCellComponentRegistry Registry = new();

    [Theory]
    [MemberData(nameof(CellCases))]
    public void Registry_backed_dynamic_component_renders_expected_output(
        ListColumnComponentKind componentKind,
        object? value,
        string selector,
        string expectedFragment)
    {
        var column = new ListColumnRenderModel(Guid.NewGuid(), "Field", "Field", Domain.FieldDataType.Text, componentKind, false);
        var parameters = new Dictionary<string, object?>
        {
            ["Column"] = column,
            ["Value"] = value
        };

        var cut = RenderComponent<DynamicComponent>(builder => builder
            .Add(p => p.Type, Registry.Resolve(componentKind))
            .Add(p => p.Parameters, parameters));

        var rendered = cut.Find(selector).TextContent;
        Assert.Contains(expectedFragment, rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Dynamic_list_source_does_not_render_payload_values_directly_in_markup()
    {
        var repositoryRoot = FindRepositoryRoot();
        var listPath = Path.Combine(repositoryRoot, "src", "Aion.AppHost", "Components", "DynamicList.razor");
        var content = File.ReadAllText(listPath);

        Assert.DoesNotContain("<td>@(payload.TryGetValue", content, StringComparison.Ordinal);
        Assert.DoesNotContain("@switch(column.ComponentKind)", content, StringComparison.Ordinal);
        Assert.Contains("<DynamicComponent Type=\"@ResolveCellComponent(column.ComponentKind)\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_resolves_all_supported_list_component_kinds()
    {
        var requiredKinds = new[]
        {
            ListColumnComponentKind.Text,
            ListColumnComponentKind.Number,
            ListColumnComponentKind.Decimal,
            ListColumnComponentKind.Boolean,
            ListColumnComponentKind.Date,
            ListColumnComponentKind.Enum,
            ListColumnComponentKind.Lookup,
            ListColumnComponentKind.Json,
            ListColumnComponentKind.File
        };

        foreach (var kind in requiredKinds)
        {
            Assert.NotNull(Registry.Resolve(kind));
        }
    }

    public static IEnumerable<object?[]> CellCases()
    {
        yield return [ListColumnComponentKind.Text, "alpha", "[data-list-cell-renderer='text']", "alpha"];
        yield return [ListColumnComponentKind.Number, 42L, "[data-list-cell-renderer='number']", "42"];
        yield return [ListColumnComponentKind.Decimal, 12.5m, "[data-list-cell-renderer='decimal']", "12.5"];
        yield return [ListColumnComponentKind.Boolean, true, "[data-list-cell-renderer='boolean']", "Oui"];
        yield return [ListColumnComponentKind.Date, new DateTime(2024, 02, 01, 14, 30, 0, DateTimeKind.Utc), "[data-list-cell-renderer='date']", "2024"];
        yield return [ListColumnComponentKind.Enum, "A", "[data-list-cell-renderer='select']", "A"];
        yield return [ListColumnComponentKind.Json, JsonDocument.Parse("{\"key\":\"value\"}").RootElement, "[data-list-cell-renderer='json']", "key"];
        yield return [ListColumnComponentKind.File, null, "[data-list-cell-renderer='file']", "—"];
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AionMemory.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
