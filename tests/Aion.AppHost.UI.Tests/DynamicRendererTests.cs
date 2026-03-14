using Aion.AppHost.Services.Rendering;
using Aion.Domain;

namespace Aion.AppHost.UI.Tests;

public sealed class DynamicRendererTests
{
    [Fact]
    public void Dynamic_form_renderer_builds_form_model_from_table()
    {
        var table = BuildTable();
        var renderer = new DynamicFormRenderer();

        var model = renderer.Render(table);

        Assert.Equal(table.Id, model.TableId);
        Assert.Equal(10, model.Fields.Count);
        Assert.Contains(model.Fields, f => f.FieldName == "Name" && f.ComponentKind == FormFieldComponentKind.Text);
        Assert.Contains(model.Fields, f => f.FieldName == "Category" && f.Options.SequenceEqual(new[] { "A", "B", "C" }));
        Assert.Contains(model.Fields, f => f.FieldName == "Attachment" && f.Placeholder == "Upload à venir");
    }

    [Fact]
    public void Dynamic_list_renderer_uses_only_visible_fields()
    {
        var table = BuildTable();
        var renderer = new DynamicListRenderer();

        var model = renderer.Render(table);

        Assert.Equal(table.Id, model.TableId);
        Assert.Equal(new[] { "Name", "Count", "Price", "Enabled" }, model.Columns.Select(c => c.FieldName).ToArray());
    }

    [Theory]
    [InlineData(FieldDataType.Text, FormFieldComponentKind.Text, ListColumnComponentKind.Text)]
    [InlineData(FieldDataType.Number, FormFieldComponentKind.Number, ListColumnComponentKind.Number)]
    [InlineData(FieldDataType.Decimal, FormFieldComponentKind.Decimal, ListColumnComponentKind.Decimal)]
    [InlineData(FieldDataType.Boolean, FormFieldComponentKind.Checkbox, ListColumnComponentKind.Boolean)]
    [InlineData(FieldDataType.Date, FormFieldComponentKind.DatePicker, ListColumnComponentKind.Date)]
    [InlineData(FieldDataType.Lookup, FormFieldComponentKind.Select, ListColumnComponentKind.Lookup)]
    [InlineData(FieldDataType.File, FormFieldComponentKind.FilePlaceholder, ListColumnComponentKind.File)]
    [InlineData(FieldDataType.Enum, FormFieldComponentKind.Select, ListColumnComponentKind.Enum)]
    [InlineData(FieldDataType.Note, FormFieldComponentKind.TextArea, ListColumnComponentKind.Note)]
    [InlineData(FieldDataType.Json, FormFieldComponentKind.JsonTextArea, ListColumnComponentKind.Json)]
    public void Renderer_mapping_covers_minimal_field_types(
        FieldDataType dataType,
        FormFieldComponentKind expectedForm,
        ListColumnComponentKind expectedList)
    {
        var table = new STable
        {
            Id = Guid.NewGuid(),
            Name = "Sample",
            Fields = new List<SFieldDefinition>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Name = "Field",
                    Label = "Field",
                    DataType = dataType,
                    IsListVisible = true
                }
            }
        };

        var form = new DynamicFormRenderer().Render(table);
        var list = new DynamicListRenderer().Render(table);

        Assert.Equal(expectedForm, form.Fields.Single().ComponentKind);
        Assert.Equal(expectedList, list.Columns.Single().ComponentKind);
    }

    private static STable BuildTable()
    {
        return new STable
        {
            Id = Guid.NewGuid(),
            Name = "Products",
            DisplayName = "Produits",
            Fields = new List<SFieldDefinition>
            {
                MakeField("Name", "Nom", FieldDataType.Text, true, true),
                MakeField("Count", "Quantité", FieldDataType.Number, true, true),
                MakeField("Price", "Prix", FieldDataType.Decimal, true, true),
                MakeField("Enabled", "Actif", FieldDataType.Boolean, true, true),
                MakeField("StartDate", "Date", FieldDataType.Date, false),
                MakeField("Owner", "Owner", FieldDataType.Lookup, false),
                MakeField("Attachment", "Pièce jointe", FieldDataType.File, false),
                MakeField("Category", "Catégorie", FieldDataType.Enum, false, enumValues: "A,B,C"),
                MakeField("Description", "Description", FieldDataType.Note, false),
                MakeField("Meta", "Meta", FieldDataType.Json, false)
            }
        };
    }

    private static SFieldDefinition MakeField(
        string name,
        string label,
        FieldDataType type,
        bool isListVisible,
        bool isSortable = false,
        string? enumValues = null)
    {
        return new SFieldDefinition
        {
            Id = Guid.NewGuid(),
            Name = name,
            Label = label,
            DataType = type,
            IsListVisible = isListVisible,
            IsSortable = isSortable,
            EnumValues = enumValues
        };
    }
}
