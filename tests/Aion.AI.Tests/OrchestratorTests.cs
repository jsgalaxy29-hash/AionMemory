using System.Collections.Generic;
using Aion.AI;
using Aion.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.AI.Tests;

public class OrchestratorTests
{
    [Fact]
    public async Task IntentRecognizer_parses_structured_response()
    {
        var provider = new StubLlmProvider("{\"intent\":\"create_note\",\"parameters\":{\"title\":\"Hello\"},\"confidence\":0.82}");
        var recognizer = new IntentRecognizer(provider, NullLogger<IntentRecognizer>.Instance);

        var result = await recognizer.DetectAsync(new IntentDetectionRequest { Input = "ajoute une note" });

        Assert.Equal("create_note", result.Intent);
        Assert.Equal(0.82, result.Confidence, 2);
        Assert.Equal("Hello", result.Parameters["title"]);
        Assert.Equal(provider.Payload, result.RawResponse);
    }

    [Fact]
    public async Task IntentRecognizer_handles_missing_context()
    {
        var provider = new StubLlmProvider("{\"intent\":\"chat\",\"parameters\":{},\"confidence\":0.5}");
        var recognizer = new IntentRecognizer(provider, NullLogger<IntentRecognizer>.Instance);

        var result = await recognizer.DetectAsync(new IntentDetectionRequest { Input = "bonjour", Context = null! });

        Assert.Equal("chat", result.Intent);
    }

    [Fact]
    public async Task CrudInterpreter_returns_structured_instruction()
    {
        var module = new S_Module
        {
            Name = "Test",
            EntityTypes =
            [
                new()
                {
                    Name = "Item",
                    Fields = new List<S_Field> { new() { Name = "Title" }, new() { Name = "Done" } }
                }
            ]
        };

        var payload = "{\"action\":\"create\",\"filters\":{\"module\":\"Test\"},\"payload\":{\"title\":\"Demo\"}}";
        var provider = new StubLlmProvider(payload);
        var interpreter = new CrudInterpreter(provider, NullLogger<CrudInterpreter>.Instance);

        var result = await interpreter.GenerateQueryAsync(new CrudQueryRequest { Intent = "ajoute", Module = module });

        Assert.Equal("create", result.Action);
        Assert.Equal("Test", result.Filters["module"]);
        Assert.Equal("Demo", result.Payload["title"]);
        Assert.Equal(payload, result.RawResponse);
    }

    private sealed class StubLlmProvider : IChatModel
    {
        public StubLlmProvider(string payload)
        {
            Payload = payload;
        }

        public string Payload { get; }

        public Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LlmResponse(Payload, Payload));
        }
    }
}
