using Aion.AI;
using Aion.Domain;
using Aion.Infrastructure.Adapters;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.Tests;

public class IntentRecognizerIntegrationTests
{
    [Fact]
    public async Task IntentRecognizer_handles_wrapped_json_payload()
    {
        const string payload = "Réponse:\n```{\"intent\":\"create_task\",\"parameters\":{\"title\":\"Planter\"},\"confidence\":0.73}```";
        var provider = new StubLlmProvider(payload);
        var recognizer = new BasicIntentDetector(provider, NullLogger<BasicIntentDetector>.Instance);

        var result = await recognizer.DetectAsync(new IntentDetectionRequest { Input = "ajoute une tâche" });

        Assert.Equal("create_task", result.Intent);
        Assert.Equal(0.73, result.Confidence, 2);
        Assert.Equal("Planter", result.Parameters["title"]);
        Assert.Equal(payload, result.RawResponse);
    }

    [Fact]
    public async Task IntentRecognizer_ignores_non_json_payload()
    {
        const string payload = "simple string response";
        var provider = new StubLlmProvider(payload);
        var recognizer = new BasicIntentDetector(provider, NullLogger<BasicIntentDetector>.Instance);

        var result = await recognizer.DetectAsync(new IntentDetectionRequest { Input = "conversation" });

        Assert.Equal("chat", result.Intent);
        Assert.Equal("conversation", result.Parameters["query"]);
        Assert.Equal(payload, result.RawResponse);
    }

    private sealed class StubLlmProvider : ILLMProvider
    {
        public StubLlmProvider(string payload)
        {
            Payload = payload;
        }

        public string Payload { get; }

        public Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse(Payload, Payload));
    }
}
