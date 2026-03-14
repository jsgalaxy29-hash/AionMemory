using Aion.AppHost.Components;
using Aion.AppHost.Services.Rendering;
using Aion.Domain;
using Bunit;

namespace Aion.AppHost.UI.Tests;

public sealed class DynamicFormFieldRegistryTests : TestContext
{
    [Fact]
    public void Dynamic_form_renders_expected_components_for_text_number_and_select()
    {
        var table = new STable
        {
            Id = Guid.NewGuid(),
            Name = "Products",
            Fields =
            [
                MakeField("Name", FieldDataType.Text),
                MakeField("Count", FieldDataType.Number),
                MakeField("Category", FieldDataType.Enum, enumValues: "A,B")
            ]
        };

        Services.AddSingleton<IDynamicFieldComponentRegistry, DynamicFieldComponentRegistry>();

        var cut = RenderComponent<DynamicForm>(parameters => parameters
            .Add(p => p.Table, table));

        Assert.NotNull(cut.Find("[data-field-renderer='text']"));
        Assert.NotNull(cut.Find("[data-field-renderer='number']"));
        Assert.NotNull(cut.Find("[data-field-renderer='select']"));
    }

    [Fact]
    public void Dynamic_form_source_does_not_switch_on_component_kind()
    {
        var repositoryRoot = FindRepositoryRoot();
        var formPath = Path.Combine(repositoryRoot, "src", "Aion.AppHost", "Components", "DynamicForm.razor");
        var content = File.ReadAllText(formPath);

        Assert.DoesNotContain("@switch (field.ComponentKind)", content, StringComparison.Ordinal);
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

    private static SFieldDefinition MakeField(string name, FieldDataType dataType, string? enumValues = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Label = name,
            DataType = dataType,
            EnumValues = enumValues
        };
}
