using System;
using System.Collections.Generic;
using Aion.Domain;
using Aion.Tests.Fixtures;

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
}
