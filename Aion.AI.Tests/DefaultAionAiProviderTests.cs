using System.Net;
using System.Net.Http;
using System.Text;
using Aion.AI;
using Aion.AI;
using Aion.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aion.AI.Tests;

public class DefaultAionAiProviderTests
{
    [Fact]
    public async Task GenerateAsync_returns_stub_when_no_endpoint_is_configured()
    {
        var provider = CreateProvider(new AionAiOptions { LlmModel = "gpt-stub" });

        var response = await provider.GenerateAsync("bonjour");

        Assert.Equal("[stub] gpt-stub: bonjour", response.Content);
        Assert.Equal("gpt-stub", response.Model);
    }

    [Fact]
    public async Task EmbedAsync_returns_deterministic_vector_in_stub_mode()
    {
        var provider = CreateProvider(new AionAiOptions());

        var vector = await provider.EmbedAsync("demo");

        Assert.Equal(8, vector.Vector.Length);
        Assert.True(vector.Vector.SequenceEqual(new[] { 4f, 5f, 6f, 7f, 8f, 9f, 10f, 11f }));
    }

    [Fact]
    public async Task TranscribeAsync_returns_stub_result_when_transcription_endpoint_missing()
    {
        var provider = CreateProvider(new AionAiOptions { TranscriptionModel = "whisper" });

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("audio"));
        var result = await provider.TranscribeAsync(stream, "file.wav");

        Assert.Equal("Transcription stub", result.Text);
        Assert.Equal("whisper", result.Model);
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

    private static DefaultAionAiProvider CreateProvider(AionAiOptions options)
    {
        var handler = new StubHttpMessageHandler();
        var factory = new StubHttpClientFactory(handler);
        return new DefaultAionAiProvider(factory, Options.Create(options), NullLogger<DefaultAionAiProvider>.Instance);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ \"choices\":[{\"message\":{\"content\":\"ok\"}}] }")
            });
    }

    private sealed class StubTextGenerationProvider : ILLMProvider
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
