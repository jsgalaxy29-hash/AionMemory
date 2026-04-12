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
        Assert.Contains(model.Fields, f => f.FieldName == "Attachment" && f.Placeholder == "Upload a venir");
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
            Name = "Sample"
        };
        table.Fields.Add(new SFieldDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Field",
            Label = "Field",
            DataType = dataType,
            IsListVisible = true
        });

        var form = new DynamicFormRenderer().Render(table);
        var list = new DynamicListRenderer().Render(table);

        Assert.Equal(expectedForm, form.Fields.Single().ComponentKind);
        Assert.Equal(expectedList, list.Columns.Single().ComponentKind);
    }

    private static STable BuildTable()
    {
        var table = new STable
        {
            Id = Guid.NewGuid(),
            Name = "Products",
            DisplayName = "Produits"
        };

        table.Fields.Add(MakeField("Name", "Nom", FieldDataType.Text, true, true));
        table.Fields.Add(MakeField("Count", "Quantite", FieldDataType.Number, true, true));
        table.Fields.Add(MakeField("Price", "Prix", FieldDataType.Decimal, true, true));
        table.Fields.Add(MakeField("Enabled", "Actif", FieldDataType.Boolean, true, true));
        table.Fields.Add(MakeField("StartDate", "Date", FieldDataType.Date, false));
        table.Fields.Add(MakeField("Owner", "Owner", FieldDataType.Lookup, false));
        table.Fields.Add(MakeField("Attachment", "Piece jointe", FieldDataType.File, false));
        table.Fields.Add(MakeField("Category", "Categorie", FieldDataType.Enum, false, enumValues: "A,B,C"));
        table.Fields.Add(MakeField("Description", "Description", FieldDataType.Note, false));
        table.Fields.Add(MakeField("Meta", "Meta", FieldDataType.Json, false));

        return table;
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
