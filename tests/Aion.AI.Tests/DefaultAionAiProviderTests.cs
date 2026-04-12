using System.Text;
using Aion.AI;
using Aion.Domain;
using Xunit;

namespace Aion.AI.Tests;

public class DefaultAionAiProviderTests
{
    [Fact]
    public async Task Mock_chat_provider_returns_stubbed_prefix()
    {
        var provider = new HttpMockChatModel();

        var response = await provider.GenerateAsync("bonjour");

        Assert.Contains("[mock-chat]", response.Content, StringComparison.Ordinal);
        Assert.Equal("mock-chat", response.Model);
    }

    [Fact]
    public void Mock_embeddings_are_deterministic()
    {
        var provider = new HttpMockEmbeddingsModel();

        var vector1 = provider.EmbedAsync("demo").Result.Vector;
        var vector2 = provider.EmbedAsync("demo").Result.Vector;

        Assert.Equal(vector1, vector2);
    }

    [Fact]
    public async Task Mock_transcription_returns_stub()
    {
        var provider = new HttpMockTranscriptionModel();

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("audio"));
        var result = await provider.TranscribeAsync(stream, "file.wav");

        Assert.Equal("[mock-transcription] file.wav", result.Text);
        Assert.Equal("mock-transcription", result.Model);
    }

    [Fact]
    public async Task DefaultModuleDesigner_parses_last_generated_payload()
    {
        var moduleJson = """
        {
          "Name": "Recettes",
          "EntityTypes": [
            {
              "Name": "Recette",
              "PluralName": "Recettes",
              "Fields": [ { "Name": "Titre", "Label": "Titre", "DataType": "Text" } ]
            }
          ]
        }
        """;

        var provider = new StubTextGenerationProvider(moduleJson);
        var designer = new DefaultModuleDesigner(provider);

        var result = await designer.GenerateModuleAsync(new ModuleDesignRequest { Prompt = "Cuisine" });

        Assert.Equal("Recettes", result.Module.Name);
        Assert.Single(result.Module.EntityTypes);
        Assert.Equal(moduleJson.Trim(), designer.LastGeneratedJson);
    }

    [Fact]
    public void Options_are_normalized_for_single_endpoint_configuration()
    {
        var options = new AionAiOptions
        {
            LlmEndpoint = " https://api.aion.local/v1 ",
            Provider = "  openai  "
        };

        options.Normalize();

        Assert.Equal("https://api.aion.local/v1", options.BaseEndpoint);
        Assert.Equal("openai", options.Provider);
    }

    private sealed class StubTextGenerationProvider : IChatModel
    {
        private readonly string _payload;

        public StubTextGenerationProvider(string payload)
        {
            _payload = payload;
        }

        public Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse(_payload, _payload));
    }
}
