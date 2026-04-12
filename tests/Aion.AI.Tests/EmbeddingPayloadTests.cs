using Aion.AI;
using Xunit;

namespace Aion.AI.Tests;

public class EmbeddingPayloadTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeInput_returns_single_space_for_empty_values(string? input)
    {
        Assert.Equal(" ", EmbeddingPayload.NormalizeInput(input));
    }

    [Fact]
    public void TryReadVector_returns_false_when_data_is_missing()
    {
        var ok = EmbeddingPayload.TryReadVector("{}", out var vector);

        Assert.False(ok);
        Assert.Empty(vector);
    }

    [Fact]
    public void TryReadVector_reads_first_embedding_vector()
    {
        const string json = """
        {
          "data": [
            { "embedding": [0.1, 0.2, 0.3] }
          ]
        }
        """;

        var ok = EmbeddingPayload.TryReadVector(json, out var vector);

        Assert.True(ok);
        Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, vector);
    }
}
