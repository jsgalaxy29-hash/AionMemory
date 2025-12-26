using Aion.AI;
using Aion.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aion.AI.Tests;

public class StructuredJsonValidationTests
{
    [Fact]
    public async Task IntentRecognizer_retries_and_falls_back_on_invalid_json()
    {
        var provider = new BrokenJsonChatModel();
        var recognizer = new IntentRecognizer(
            provider,
            NullLogger<IntentRecognizer>.Instance,
            Options.Create(new AionAiOptions()),
            new NoopOperationScopeFactory());

        var result = await recognizer.DetectAsync(new IntentDetectionRequest { Input = "Analyse ça" });

        Assert.Equal("unknown", result.Intent);
        Assert.Equal(3, provider.CallCount);
    }

    private sealed class BrokenJsonChatModel : IChatModel
    {
        public int CallCount { get; private set; }

        public Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        {
            CallCount++;
            const string payload = "{not-valid-json";
            return Task.FromResult(new LlmResponse(payload, payload));
        }
    }
}
