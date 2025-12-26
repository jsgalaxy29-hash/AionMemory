using Aion.AI;
using Aion.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aion.AI.Tests;

public class IntentDetectorGoldenTests
{
    [Theory]
    [InlineData("Planifie un rendez-vous demain à 9h", IntentCatalog.Agenda)]
    [InlineData("Améliore cette note : Acheter des graines", IntentCatalog.Note)]
    [InlineData("Génère un rapport hebdo", IntentCatalog.Report)]
    [InlineData("Crée un module pour suivre mes plantations", IntentCatalog.Module)]
    [InlineData("Liste les contacts sans email", IntentCatalog.Data)]
    public async Task IntentRecognizer_maps_prompts_to_expected_intent(string input, string expectedIntent)
    {
        var provider = new StubProvider("no-json");
        var recognizer = new IntentRecognizer(
            provider,
            NullLogger<IntentRecognizer>.Instance,
            Options.Create(new AionAiOptions()),
            new NoopOperationScopeFactory());

        var result = await recognizer.DetectAsync(new IntentDetectionRequest { Input = input });

        Assert.Equal(expectedIntent, result.Intent);
        Assert.Equal("heuristic", result.Parameters["fallback"]);
    }

    private sealed class StubProvider : IChatModel
    {
        private readonly string _payload;

        public StubProvider(string payload)
        {
            _payload = payload;
        }

        public Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse(_payload, _payload));
    }
}
