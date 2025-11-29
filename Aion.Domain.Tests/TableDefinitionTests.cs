using Aion.Domain;
using Xunit;

namespace Aion.Domain.Tests;

public class TableDefinitionTests
{
    [Fact]
    public void Text_factory_sets_expected_properties()
    {
        var field = SFieldDefinition.Text("Name", "Nom", required: true, defaultValue: "John");

        Assert.Equal("Name", field.Name);
        Assert.Equal("Nom", field.Label);
        Assert.Equal(EnumSFieldType.String, field.DataType);
        Assert.True(field.IsRequired);
        Assert.Equal("John", field.DefaultValue);
    }

    [Fact]
    public void Create_builds_table_with_fields()
    {
        var fields = new[]
        {
            new SFieldDefinition { Name = "Title", Label = "Titre", DataType = EnumSFieldType.String },
            new SFieldDefinition { Name = "Done", Label = "Terminée", DataType = EnumSFieldType.Bool }
        };

        var table = STable.Create("tasks", "Tâches", fields);

        Assert.Equal("tasks", table.Name);
        Assert.Equal("Tâches", table.DisplayName);
        Assert.Equal(2, table.Fields.Count);
        Assert.All(table.Fields, f => Assert.NotEqual(Guid.Empty, f.Id));
    }
}
