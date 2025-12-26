using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Aion.Infrastructure.ModuleBuilder;
using Aion.Infrastructure.Observability;
using Aion.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.Tests;

public class EndToEndSmokeTests : IClassFixture<SqliteInMemoryFixture>
{
    private readonly SqliteInMemoryFixture _fixture;

    public EndToEndSmokeTests(SqliteInMemoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Module_applier_and_data_engine_smoke_path_is_healthy()
    {
        await using var context = _fixture.CreateContext();
        var validator = new ModuleValidator(context, NullLogger<ModuleValidator>.Instance);
        var applier = new ModuleApplier(
            context,
            validator,
            NullLogger<ModuleApplier>.Instance,
            new OperationScopeFactory(),
            new NullSecurityAuditService());

        var spec = BuildSmokeSpec();
        var appliedTables = await applier.ApplyAsync(spec);
        var applied = Assert.Single(appliedTables);
        Assert.Equal("notes", applied.Name);
        Assert.NotEmpty(applied.Fields);

        var engine = _fixture.CreateDataEngine();
        var table = (await engine.GetTablesAsync()).Single(t => t.Name == "notes");
        Assert.Contains(table.Views, view => view.IsDefault);

        await engine.InsertAsync(table.Id, new Dictionary<string, object?> { ["title"] = "First note", ["priority"] = 1 });
        await engine.InsertAsync(table.Id, new Dictionary<string, object?> { ["title"] = "Gamma entry", ["priority"] = 3 });
        await engine.InsertAsync(table.Id, new Dictionary<string, object?> { ["title"] = "Beta memo", ["priority"] = 2 });

        var paged = (await engine.QueryAsync(table.Id, new QuerySpec
        {
            OrderBy = "priority",
            Descending = true,
            Skip = 1,
            Take = 1
        })).ToList();

        var middle = Assert.Single(paged);
        Assert.Equal("Beta memo", ReadField(middle, "title"));

        var fullText = (await engine.QueryAsync(table.Id, new QuerySpec { FullText = "Gamma" })).ToList();
        var matched = Assert.Single(fullText);
        Assert.Equal("Gamma entry", ReadField(matched, "title"));
    }

    private static ModuleSpec BuildSmokeSpec()
        => new()
        {
            Slug = "smoke-module",
            DisplayName = "Smoke module",
            Tables =
            {
                new TableSpec
                {
                    Slug = "notes",
                    DisplayName = "Notes",
                    Fields = new List<FieldSpec>
                    {
                        new() { Slug = "title", Label = "Title", DataType = ModuleFieldDataTypes.Text, IsRequired = true, IsSearchable = true, IsListVisible = true },
                        new() { Slug = "priority", Label = "Priority", DataType = ModuleFieldDataTypes.Number, IsSortable = true, IsFilterable = true }
                    },
                    Views = new List<ViewSpec>
                    {
                        new() { Slug = "all", DisplayName = "All", Sort = "priority desc", IsDefault = true }
                    }
                }
            }
        };

    private static string? ReadField(F_Record record, string fieldName)
    {
        using var document = JsonDocument.Parse(record.DataJson);
        return document.RootElement.TryGetProperty(fieldName, out var property)
            ? property.GetString()
            : null;
    }
}
