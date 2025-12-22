using Aion.AI;
using Aion.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.AI.Tests;

public class MemoryAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_parses_structured_json()
    {
        var recordA = new MemoryRecord(Guid.NewGuid(), "Note A", "Contenu", "note");
        var recordB = new MemoryRecord(Guid.NewGuid(), "Note B", "Autre", "event");
        var payload = $$"""
        {
            "summary": "Synthèse courte",
            "topics": [ { "name": "Projet", "keywords": ["planning", "roadmap"] } ],
            "links": [ { "fromId": "{recordA.Id}", "toId": "{recordB.Id}", "reason": "Même projet", "fromType": "note", "toType": "event" } ]
        }
        """;

        var analyzer = new MemoryAnalyzer(new StubChatModel(payload), NullLogger<MemoryAnalyzer>.Instance);
        var result = await analyzer.AnalyzeAsync(new MemoryAnalysisRequest(new[] { recordA, recordB }, locale: "fr-FR", scope: "demo"));

        Assert.Equal("Synthèse courte", result.Summary);
        var topic = Assert.Single(result.Topics);
        Assert.Equal("Projet", topic.Name);
        Assert.Contains("planning", topic.Keywords);
        var link = Assert.Single(result.SuggestedLinks);
        Assert.Equal(recordA.Id, link.FromId);
        Assert.Equal(recordB.Id, link.ToId);
        Assert.Equal("Même projet", link.Reason);
    }

    [Fact]
    public async Task AnalyzeAsync_returns_fallback_when_unparsable()
    {
        var record = new MemoryRecord(Guid.NewGuid(), "Titre", "Contenu", "note");
        var analyzer = new MemoryAnalyzer(new StubChatModel("[stub-invalid]"), NullLogger<MemoryAnalyzer>.Instance);

        var result = await analyzer.AnalyzeAsync(new MemoryAnalysisRequest(new[] { record }));

        Assert.NotEmpty(result.Summary);
        Assert.Empty(result.Topics);
        Assert.Empty(result.SuggestedLinks);
        Assert.Equal("[stub-invalid]", result.RawResponse);
    }

    [Fact]
    public async Task MockMemoryAnalyzer_returns_structured_result()
    {
        var records = new[]
        {
            new MemoryRecord(Guid.NewGuid(), "Journal", "Texte", "note"),
            new MemoryRecord(Guid.NewGuid(), "Rappel", "Texte", "event")
        };

        var analyzer = new MockMemoryAnalyzer();
        var result = await analyzer.AnalyzeAsync(new MemoryAnalysisRequest(records));

        Assert.Contains("Mock summary", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(result.Topics);
        Assert.NotEmpty(result.SuggestedLinks);
    }

    private sealed class StubChatModel : IChatModel
    {
        private readonly string _payload;

        public StubChatModel(string payload)
        {
            _payload = payload;
        }

        public Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LlmResponse(_payload, _payload, "stub"));
        }
    }
}
