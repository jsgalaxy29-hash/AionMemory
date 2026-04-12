using System.Text.Json;
using Aion.Domain.Logic;
using Xunit;

namespace Aion.Domain.Tests;

public class DynamicListLogicTests
{
    [Fact]
    public void DeserializePayload_returns_empty_dictionary_for_empty_json()
    {
        var payload = DynamicListLogic.DeserializePayload(string.Empty);

        Assert.Empty(payload);
    }

    [Fact]
    public void DeserializePayload_returns_raw_when_invalid_json()
    {
        const string invalid = "{invalid";

        var payload = DynamicListLogic.DeserializePayload(invalid);

        Assert.Single(payload);
        Assert.Equal(invalid, payload["raw"]);
    }

    [Fact]
    public void DeserializePayload_reads_fields()
    {
        var payload = DynamicListLogic.DeserializePayload("{ \"title\": \"Note\", \"count\": 2 }");

        var title = Assert.IsType<JsonElement>(payload["title"]);
        var count = Assert.IsType<JsonElement>(payload["count"]);

        Assert.Equal("Note", title.GetString());
        Assert.Equal(2, count.GetInt32());
    }
}
