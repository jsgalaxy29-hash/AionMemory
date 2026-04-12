using System;
using System.Collections.Generic;
using System.Linq;
using Aion.AI;
using Aion.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.AI.Tests;

public class AssistantContextTests
{
    [Fact]
    public async Task MemoryContextBuilder_CombinesSources()
    {
        var search = new FakeSearchService([
            new SearchHit("Record", Guid.NewGuid(), "Note", "Snippet A", 0.9),
            new SearchHit("Record", Guid.NewGuid(), "Note B", "Snippet B", 0.2)
        ]);
        var historyId = Guid.NewGuid();
        var life = new FakeLifeService([
            new S_HistoryEvent { Id = historyId, Title = "demo event", Description = "something demo", OccurredAt = DateTimeOffset.UtcNow }
        ]);
        var insightId = Guid.NewGuid();
        var intelligence = new FakeMemoryIntelligenceService([
            new MemoryInsight { Id = insightId, Summary = "demo insight", Scope = "demo", GeneratedAt = DateTimeOffset.UtcNow }
        ]);

        var builder = new MemoryContextBuilder(search, life, intelligence, NullLogger<MemoryContextBuilder>.Instance);
        var result = await builder.BuildAsync(new MemoryContextRequest { Query = "demo" });

        Assert.NotEmpty(result.Records);
        Assert.Contains(result.History, h => h.RecordId == historyId);
        Assert.Contains(result.Insights, i => i.RecordId == insightId);
    }

    [Fact]
    public async Task ChatAnswerer_ReturnsFallbackWhenContextEmpty()
    {
        var contextBuilder = new StubContextBuilder(new MemoryContextResult(Array.Empty<MemoryContextItem>(), Array.Empty<MemoryContextItem>(), Array.Empty<MemoryContextItem>()));
        var chat = new StubChatModel("{}");
        var answerer = new ChatAnswerer(chat, contextBuilder, NullLogger<ChatAnswerer>.Instance);

        var answer = await answerer.AnswerAsync(new AssistantAnswerRequest("demo"));

        Assert.True(answer.UsedFallback);
        Assert.Contains("aucune information", answer.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(answer.Citations);
    }

    [Fact]
    public async Task ChatAnswerer_ParsesCitations()
    {
        var citedId = Guid.NewGuid();
        var context = new MemoryContextResult(
            new[] { new MemoryContextItem(citedId, "record", "Title", "Snippet", DateTimeOffset.UtcNow, Score: 0.9) },
            Array.Empty<MemoryContextItem>(),
            Array.Empty<MemoryContextItem>()
        );

        var contextBuilder = new StubContextBuilder(context);
        var chat = new StubChatModel($"{{\"message\":\"ok\",\"citations\":[\"{citedId}\",\"{Guid.NewGuid()}\"],\"fallback\":false}}");
        var answerer = new ChatAnswerer(chat, contextBuilder, NullLogger<ChatAnswerer>.Instance);

        var answer = await answerer.AnswerAsync(new AssistantAnswerRequest("demo"));

        Assert.False(answer.UsedFallback);
        Assert.Single(answer.Citations);
        Assert.Equal(citedId, answer.Citations.Single());
        Assert.Equal("ok", answer.Message);
    }

    private sealed class FakeSearchService : ISearchService
    {
        private readonly IReadOnlyCollection<SearchHit> _hits;

        public FakeSearchService(IReadOnlyCollection<SearchHit> hits)
        {
            _hits = hits;
        }

        public Task<IEnumerable<SearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<SearchHit>>(_hits);

        public Task IndexNoteAsync(S_Note note, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task IndexRecordAsync(F_Record record, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task IndexFileAsync(F_File file, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeLifeService : ILifeService
    {
        private readonly List<S_HistoryEvent> _events;

        public FakeLifeService(IEnumerable<S_HistoryEvent> events)
        {
            _events = events.ToList();
        }

        public Task<S_HistoryEvent> AddHistoryAsync(S_HistoryEvent evt, CancellationToken cancellationToken = default)
        {
            _events.Add(evt);
            return Task.FromResult(evt);
        }

        public Task<TimelinePage> GetTimelinePageAsync(TimelineQuery query, CancellationToken cancellationToken = default)
        {
            var items = _events
                .Skip(query.NormalizedSkip)
                .Take(query.NormalizedTake)
                .ToList();
            var hasMore = _events.Count > query.NormalizedSkip + items.Count;
            return Task.FromResult(new TimelinePage(items, hasMore, query.NormalizedSkip + items.Count));
        }

        public Task<IEnumerable<S_HistoryEvent>> GetTimelineAsync(DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<S_HistoryEvent>>(_events);
    }

    private sealed class FakeMemoryIntelligenceService : IMemoryIntelligenceService
    {
        private readonly IReadOnlyCollection<MemoryInsight> _insights;

        public FakeMemoryIntelligenceService(IEnumerable<MemoryInsight> insights)
        {
            _insights = insights.ToArray();
        }

        public Task<MemoryInsight> AnalyzeAsync(MemoryAnalysisRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(_insights.First());

        public Task<IReadOnlyCollection<MemoryInsight>> GetRecentAsync(int take = 20, CancellationToken cancellationToken = default)
            => Task.FromResult(_insights);
    }

    private sealed class StubContextBuilder : IMemoryContextBuilder
    {
        private readonly MemoryContextResult _result;

        public StubContextBuilder(MemoryContextResult result)
        {
            _result = result;
        }

        public Task<MemoryContextResult> BuildAsync(MemoryContextRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private sealed class StubChatModel : IChatModel
    {
        private readonly string _payload;

        public StubChatModel(string payload)
        {
            _payload = payload;
        }

        public Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse(_payload, _payload));
    }
}
