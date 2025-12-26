using System;
using System.Collections.Generic;
using Aion.AI;
using Aion.Domain;
using Aion.Infrastructure.Services;
using Aion.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aion.Tests;

public sealed class DataEngineSearchTests : IClassFixture<SqliteInMemoryFixture>
{
    private readonly SqliteInMemoryFixture _fixture;

    public DataEngineSearchTests(SqliteInMemoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SearchAsync_uses_fts_ranking_and_snippets()
    {
        var table = new STable
        {
            Name = "tasks",
            DisplayName = "Tasks",
            Fields =
            [
                new() { Name = "Title", Label = "Titre", DataType = FieldDataType.Text },
                new() { Name = "Body", Label = "Corps", DataType = FieldDataType.Note }
            ]
        };

        var engine = _fixture.CreateDataEngine();
        await engine.CreateTableAsync(table);

        var primary = await engine.InsertAsync(table.Id, new Dictionary<string, object?>
        {
            ["Title"] = "Alpha Omega",
            ["Body"] = "Critical alpha omega signal detected"
        });

        await engine.InsertAsync(table.Id, new Dictionary<string, object?>
        {
            ["Title"] = "Alpha only",
            ["Body"] = "Background alpha note"
        });

        await engine.InsertAsync(table.Id, new Dictionary<string, object?>
        {
            ["Title"] = "Omega only",
            ["Body"] = "Omega status update"
        });

        var hits = (await engine.SearchAsync(
            table.Id,
            "alpha OR omega",
            new SearchOptions
            {
                Take = 2,
                HighlightBefore = "<b>",
                HighlightAfter = "</b>",
                SnippetTokens = 6
            })).ToList();

        Assert.Equal(2, hits.Count);
        Assert.Equal(primary.Id, hits[0].RecordId);
        Assert.True(hits[0].Score >= hits[1].Score);
        Assert.Contains("<b>", hits[0].Snippet, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(hits[1].Snippet));
    }

    [Fact]
    public async Task SearchSmartAsync_combines_semantic_results_with_full_text()
    {
        var table = new STable
        {
            Name = "garden",
            DisplayName = "Potager",
            Fields =
            [
                new() { Name = "Title", Label = "Titre", DataType = FieldDataType.Text },
                new() { Name = "Body", Label = "Corps", DataType = FieldDataType.Note }
            ]
        };

        var engine = _fixture.CreateDataEngine(embeddingProvider: new KeywordEmbeddingProvider());
        await engine.CreateTableAsync(table);

        var primary = await engine.InsertAsync(table.Id, new Dictionary<string, object?>
        {
            ["Title"] = "Tâches potager urgentes",
            ["Body"] = "Planifier et arroser le potager aujourd'hui"
        });

        var semanticOnly = await engine.InsertAsync(table.Id, new Dictionary<string, object?>
        {
            ["Title"] = "Orchard follow-up",
            ["Body"] = "Urgent actions in the orchard before the rain"
        });

        await engine.InsertAsync(table.Id, new Dictionary<string, object?>
        {
            ["Title"] = "Divers",
            ["Body"] = "Note générique sans mots-clés"
        });

        var hits = (await engine.SearchSmartAsync(table.Id, "potager urgent", new SearchOptions { Take = 3 })).ToList();

        Assert.Equal(2, hits.Count);
        Assert.Equal(primary.Id, hits[0].RecordId);
        Assert.Contains(hits, h => h.RecordId == semanticOnly.Id);
    }

    [Fact]
    public async Task SearchSmartAsync_falls_back_to_full_text_without_embeddings()
    {
        var table = new STable
        {
            Name = "notes",
            DisplayName = "Notes",
            Fields =
            [
                new() { Name = "Title", Label = "Titre", DataType = FieldDataType.Text },
                new() { Name = "Body", Label = "Corps", DataType = FieldDataType.Note }
            ]
        };

        var engine = _fixture.CreateDataEngine();
        await engine.CreateTableAsync(table);

        var primary = await engine.InsertAsync(table.Id, new Dictionary<string, object?>
        {
            ["Title"] = "Alpha memo",
            ["Body"] = "Critical alpha update"
        });

        await engine.InsertAsync(table.Id, new Dictionary<string, object?>
        {
            ["Title"] = "Beta memo",
            ["Body"] = "Secondary beta details"
        });

        var classicHits = (await engine.SearchAsync(table.Id, "alpha", new SearchOptions { Take = 2 })).ToList();
        var smartHits = (await engine.SearchSmartAsync(table.Id, "alpha", new SearchOptions { Take = 2 })).ToList();

        Assert.Equal(classicHits.Count, smartHits.Count);
        Assert.Equal(primary.Id, smartHits[0].RecordId);
        Assert.Equal(classicHits[0].RecordId, smartHits[0].RecordId);
        Assert.All(smartHits, hit => Assert.False(string.IsNullOrWhiteSpace(hit.Snippet)));
    }

    [Fact]
    public async Task SearchAsync_includes_lookup_labels_and_computed_fields()
    {
        var contacts = new STable
        {
            Name = "contacts",
            DisplayName = "Contacts",
            RowLabelTemplate = "{{First}} {{Last}}",
            Fields =
            [
                new() { Name = "First", Label = "Prénom", DataType = FieldDataType.Text },
                new() { Name = "Last", Label = "Nom", DataType = FieldDataType.Text }
            ]
        };

        var tasks = new STable
        {
            Name = "tasks",
            DisplayName = "Tasks",
            Fields =
            [
                new() { Name = "Title", Label = "Titre", DataType = FieldDataType.Text },
                new()
                {
                    Name = "Assignee",
                    Label = "Assigné",
                    DataType = FieldDataType.Lookup,
                    LookupTarget = "contacts"
                },
                new() { Name = "ReferenceCode", Label = "Code", DataType = FieldDataType.Number },
                new()
                {
                    Name = "ReferenceLabel",
                    Label = "Référence",
                    DataType = FieldDataType.Text,
                    IsComputed = true,
                    ComputedExpression = "concat('Ticket ', ReferenceCode)"
                }
            ]
        };

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        await using var searchContext = _fixture.CreateContext();
        var recordIndex = new RecordSearchIndexService(searchContext, NullLogger<RecordSearchIndexService>.Instance);
        var searchService = new SemanticSearchService(searchContext, NullLogger<SemanticSearchService>.Instance, serviceProvider, recordIndex);
        var engine = _fixture.CreateDataEngine(search: searchService);

        await engine.CreateTableAsync(contacts);
        await engine.CreateTableAsync(tasks);

        var contact = await engine.InsertAsync(contacts.Id, new Dictionary<string, object?>
        {
            ["First"] = "Ada",
            ["Last"] = "Lovelace"
        });

        var task = await engine.InsertAsync(tasks.Id, new Dictionary<string, object?>
        {
            ["Title"] = "Engine diagnostics",
            ["Assignee"] = contact.Id,
            ["ReferenceCode"] = 42
        });

        var lookupHits = (await engine.SearchAsync(tasks.Id, "Ada", new SearchOptions { Take = 5 })).ToList();
        Assert.Contains(lookupHits, hit => hit.RecordId == task.Id);

        var computedHits = (await engine.SearchAsync(tasks.Id, "Ticket", new SearchOptions { Take = 5 })).ToList();
        Assert.Contains(computedHits, hit => hit.RecordId == task.Id);
    }

    private sealed class KeywordEmbeddingProvider : IEmbeddingProvider
    {
        private static readonly string[][] KeywordMap =
        [
            ["potager", "garden", "orchard"],
            ["urgent"],
            ["alpha"]
        ];

        public Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            var vector = new float[KeywordMap.Length];

            for (var i = 0; i < KeywordMap.Length; i++)
            {
                foreach (var keyword in KeywordMap[i])
                {
                    if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        vector[i] = 1f;
                        break;
                    }
                }
            }

            return Task.FromResult(new EmbeddingResult(vector, "keyword-embeddings"));
        }
    }
}
