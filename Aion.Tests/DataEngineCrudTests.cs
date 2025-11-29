using Aion.Domain;
using Aion.Infrastructure.Services;
using Aion.Tests.Fixtures;
using Xunit;

namespace Aion.Tests;

public class DataEngineCrudTests : IClassFixture<SqliteInMemoryFixture>
{
    private readonly SqliteInMemoryFixture _fixture;

    public DataEngineCrudTests(SqliteInMemoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DataEngine_supports_table_and_record_lifecycle()
    {
        var table = new STable
        {
            Name = "tasks",
            DisplayName = "Tâches",
            Fields =
            [
                new() { Name = "Title", Label = "Titre", DataType = FieldDataType.Text, IsRequired = true },
                new() { Name = "IsDone", Label = "Terminé", DataType = FieldDataType.Boolean }
            ],
            Views =
            [
                new() { Name = "open", QueryDefinition = "{ \"IsDone\": \"false\" }" }
            ]
        };

        var engine = _fixture.CreateDataEngine();
        var created = await engine.CreateTableAsync(table);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.All(created.Fields, f => Assert.Equal(created.Id, f.TableId));

        var fetchedTable = await engine.GetTableAsync(created.Id);
        Assert.Equal(2, fetchedTable?.Fields.Count);

        var record = await engine.InsertAsync(created.Id, "{ \"Title\": \"Demo\", \"IsDone\": false }");
        Assert.Equal(created.Id, record.EntityTypeId);

        var loaded = await engine.GetAsync(created.Id, record.Id);
        Assert.NotNull(loaded);
        Assert.Contains("\"Demo\"", loaded!.DataJson);

        var updated = await engine.UpdateAsync(created.Id, record.Id, "{ \"Title\": \"Updated\", \"IsDone\": true }");
        Assert.Contains("Updated", updated.DataJson);

        var filtered = await engine.QueryAsync(created.Id, filter: "open");
        Assert.Empty(filtered);

        await engine.DeleteAsync(created.Id, record.Id);
        var deleted = await engine.GetAsync(created.Id, record.Id);
        Assert.Null(deleted);
    }
}
