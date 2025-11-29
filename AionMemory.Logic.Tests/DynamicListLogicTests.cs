using AionMemory.Logic;
using Xunit;

namespace AionMemory.Logic.Tests;

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

        Assert.Equal("Note", payload["title"]);
        Assert.Equal(2, Convert.ToInt32(payload["count"]));
    }
}
