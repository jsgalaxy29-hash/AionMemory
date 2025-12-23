using System.Collections.Generic;
using System.Linq;
using Aion.Domain;
using Aion.Tests.Fixtures;
using Xunit;

namespace Aion.Tests;

public sealed class DataEngineKnowledgeGraphTests : IClassFixture<SqliteInMemoryFixture>
{
    private readonly SqliteInMemoryFixture _fixture;

    public DataEngineKnowledgeGraphTests(SqliteInMemoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LinkRecords_creates_edges_and_nodes()
    {
        var table = CreateSimpleTable("knowledge_items");
        var engine = _fixture.CreateDataEngine();
        await engine.CreateTableAsync(table);

        var source = await engine.InsertAsync(table.Id, new Dictionary<string, object?> { ["Title"] = "Source" });
        var target = await engine.InsertAsync(table.Id, new Dictionary<string, object?> { ["Title"] = "Target" });

        var edge = await engine.LinkRecordsAsync(table.Id, source.Id, table.Id, target.Id, KnowledgeRelationType.RelatedTo);

        Assert.Equal(KnowledgeRelationType.RelatedTo, edge.RelationType);

        var slice = await engine.GetKnowledgeGraphAsync(table.Id, source.Id, depth: 1);

        Assert.Equal("Source", slice.Root.Title);
        Assert.Equal(2, slice.Nodes.Count);
        Assert.Single(slice.Edges);
        Assert.Contains(slice.Edges, e => e.FromNodeId == edge.FromNodeId && e.ToNodeId == edge.ToNodeId);
    }

    [Fact]
    public async Task GetKnowledgeGraph_respects_depth()
    {
        var table = CreateSimpleTable("graph_depth");
        var engine = _fixture.CreateDataEngine();
        await engine.CreateTableAsync(table);

        var root = await engine.InsertAsync(table.Id, new Dictionary<string, object?> { ["Title"] = "Root" });
        var middle = await engine.InsertAsync(table.Id, new Dictionary<string, object?> { ["Title"] = "Middle" });
        var leaf = await engine.InsertAsync(table.Id, new Dictionary<string, object?> { ["Title"] = "Leaf" });

        await engine.LinkRecordsAsync(table.Id, root.Id, table.Id, middle.Id, KnowledgeRelationType.LinkedTo);
        await engine.LinkRecordsAsync(table.Id, middle.Id, table.Id, leaf.Id, KnowledgeRelationType.DependsOn);

        var shallow = await engine.GetKnowledgeGraphAsync(table.Id, root.Id, depth: 1);
        Assert.Equal(2, shallow.Nodes.Count);

        var deep = await engine.GetKnowledgeGraphAsync(table.Id, root.Id, depth: 2);
        Assert.Equal(3, deep.Nodes.Count);
        Assert.Equal(2, deep.Edges.Count);
        Assert.Contains(deep.Nodes, n => n.Title == "Leaf");
    }

    private static STable CreateSimpleTable(string name)
        => new()
        {
            Name = name,
            DisplayName = name,
            Fields =
            [
                new() { Name = "Title", Label = "Title", DataType = FieldDataType.Text, IsRequired = true, IsSearchable = true }
            ]
        };
}
