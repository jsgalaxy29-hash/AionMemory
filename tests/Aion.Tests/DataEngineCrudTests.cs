using System.Linq;
using System.Text.Json;
using Aion.Domain;
using Aion.Infrastructure.Services;
using Aion.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aion.Tests;

public class DataEngineCrudTests : IClassFixture<SqliteInMemoryFixture>
{
    private readonly SqliteInMemoryFixture _fixture;

    public DataEngineCrudTests(SqliteInMemoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DataEngine_handles_lifecycle_with_views_and_soft_delete()
    {
        var table = new STable
        {
            Name = "tasks",
            DisplayName = "Tâches",
            SupportsSoftDelete = true,
            Fields =
            [
                new() { Name = "Title", Label = "Titre", DataType = FieldDataType.Text, IsRequired = true, IsSortable = true },
                new() { Name = "Status", Label = "Statut", DataType = FieldDataType.Text, IsSortable = true }
            ],
            Views =
            [
                new() { Name = "open", DisplayName = "Ouvertes", QueryDefinition = "{ \"Status\": \"todo\" }" }
            ]
        };

        var engine = _fixture.CreateDataEngine();
        await engine.CreateTableAsync(table);

        var created = await engine.InsertAsync(table.Id, new Dictionary<string, object?>
        {
            ["Title"] = "Tâche initiale",
            ["Status"] = "todo"
        });

        var loaded = await engine.GetAsync(table.Id, created.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Tâche initiale", ReadField(loaded!, "Title"));

        await engine.UpdateAsync(table.Id, created.Id, new Dictionary<string, object?>
        {
            ["Title"] = "Mise à jour",
            ["Status"] = "done"
        });

        var filtered = await engine.QueryAsync(table.Id, new QuerySpec { View = "open" });
        Assert.Empty(filtered);

        await engine.DeleteAsync(table.Id, created.Id);
        var deleted = await engine.GetAsync(table.Id, created.Id);
        Assert.Null(deleted);
        Assert.Equal(0, await engine.CountAsync(table.Id));
    }

    [Fact]
    public async Task DataEngine_enforces_constraints_and_uniqueness()
    {
        var table = new STable
        {
            Name = "rules",
            DisplayName = "Règles",
            Fields =
            [
                new() { Name = "Title", Label = "Titre", DataType = FieldDataType.Text, IsRequired = true, MinLength = 3, MaxLength = 64 },
                new() { Name = "Category", Label = "Catégorie", DataType = FieldDataType.Text, EnumValues = "work,home" },
                new() { Name = "Importance", Label = "Importance", DataType = FieldDataType.Decimal, MinValue = 0, MaxValue = 10 },
                new() { Name = "Slug", Label = "Slug", DataType = FieldDataType.Text, IsUnique = true, IsRequired = true }
            ]
        };

        var engine = _fixture.CreateDataEngine();
        await engine.CreateTableAsync(table);

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.InsertAsync(table.Id, new Dictionary<string, object?>
        {
            ["Category"] = "work",
            ["Importance"] = 1.5m,
            ["Slug"] = "missing-title"
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.InsertAsync(table.Id, new Dictionary<string, object?>
        {
            ["Title"] = "Hi",
            ["Category"] = "work",
            ["Importance"] = 1.5m,
            ["Slug"] = "short-title"
        }));

        var record = await engine.InsertAsync(table.Id, new Dictionary<string, object?>
        {
            ["Title"] = "Titre valide",
            ["Category"] = "work",
            ["Importance"] = 2.5m,
            ["Slug"] = "unique-slug"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.InsertAsync(table.Id, new Dictionary<string, object?>
        {
            ["Title"] = "Autre règle",
            ["Category"] = "home",
            ["Importance"] = 3m,
            ["Slug"] = "unique-slug"
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.UpdateAsync(table.Id, record.Id, new Dictionary<string, object?>
        {
            ["Title"] = "Titre valide",
            ["Category"] = "unknown",
            ["Importance"] = 12m,
            ["Slug"] = "unique-slug"
        }));
    }

    [Fact]
    public async Task DataEngine_supports_pagination_sorting_and_full_text()
    {
        var table = new STable
        {
            Name = "notes",
            DisplayName = "Notes",
            Fields =
            [
                new() { Name = "Title", Label = "Titre", DataType = FieldDataType.Text, IsSortable = true },
                new() { Name = "Priority", Label = "Priorité", DataType = FieldDataType.Number, IsSortable = true }
            ]
        };

        var engine = _fixture.CreateDataEngine();
        await engine.CreateTableAsync(table);

        await engine.InsertAsync(table.Id, new Dictionary<string, object?> { ["Title"] = "Alpha", ["Priority"] = 1 });
        await engine.InsertAsync(table.Id, new Dictionary<string, object?> { ["Title"] = "Gamma note", ["Priority"] = 3 });
        await engine.InsertAsync(table.Id, new Dictionary<string, object?> { ["Title"] = "Beta", ["Priority"] = 2 });

        var paged = await engine.QueryAsync(table.Id, new QuerySpec
        {
            OrderBy = "Priority",
            Descending = true,
            Skip = 1,
            Take = 1
        });

        Assert.Single(paged);
        Assert.Equal("Beta", ReadField(paged.First(), "Title"));

        var filtered = await engine.QueryAsync(table.Id, new QuerySpec
        {
            Filters = { new QueryFilter("Title", QueryFilterOperator.Equals, "Gamma note") }
        });
        Assert.Single(filtered);

        var range = await engine.QueryAsync(table.Id, new QuerySpec
        {
            Filters = { new QueryFilter("Priority", QueryFilterOperator.GreaterThan, 1) },
            OrderBy = "Priority"
        });

        Assert.Equal(new[] { "Beta", "Gamma note" }, range.Select(r => ReadField(r, "Title")));

        var contains = await engine.QueryAsync(table.Id, new QuerySpec
        {
            Filters = { new QueryFilter("Title", QueryFilterOperator.Contains, "note") }
        });

        Assert.Single(contains);

        var fts = await engine.QueryAsync(table.Id, new QuerySpec { FullText = "Gamma" });
        Assert.Single(fts);
        Assert.Equal("Gamma note", ReadField(fts.First(), "Title"));
    }

    [Fact]
    public async Task DataEngine_tracks_record_history_across_lifecycle()
    {
        var table = new STable
        {
            Name = "history",
            DisplayName = "History",
            Fields =
            [
                new() { Name = "Title", Label = "Titre", DataType = FieldDataType.Text, IsRequired = true }
            ]
        };

        var engine = _fixture.CreateDataEngine();
        await engine.CreateTableAsync(table);

        var record = await engine.InsertAsync(table.Id, new Dictionary<string, object?> { ["Title"] = "Initial" });
        await engine.UpdateAsync(table.Id, record.Id, new Dictionary<string, object?> { ["Title"] = "Updated" });
        await engine.DeleteAsync(table.Id, record.Id);

        var history = (await engine.GetHistoryAsync(table.Id, record.Id)).ToList();

        Assert.Equal(3, history.Count);

        Assert.Collection(history,
            change =>
            {
                Assert.Equal(ChangeType.Create, change.ChangeType);
                Assert.Equal(1, change.Version);
                Assert.Contains("Initial", change.DataJson);
            },
            change =>
            {
                Assert.Equal(ChangeType.Update, change.ChangeType);
                Assert.Equal(2, change.Version);
                Assert.NotNull(change.PreviousDataJson);
                Assert.Contains("Updated", change.DataJson);
                Assert.Contains("Initial", change.PreviousDataJson!);
            },
            change =>
            {
                Assert.Equal(ChangeType.Delete, change.ChangeType);
                Assert.Equal(3, change.Version);
                Assert.Contains("Updated", change.DataJson);
            });
    }

    [Fact]
    public async Task DataEngine_audits_record_operations_with_current_user()
    {
        var table = new STable
        {
            Name = "audit",
            DisplayName = "Audit",
            Fields =
            [
                new() { Name = "Title", Label = "Titre", DataType = FieldDataType.Text, IsRequired = true }
            ]
        };

        var engine = _fixture.CreateDataEngine();
        await engine.CreateTableAsync(table);

        var record = await engine.InsertAsync(table.Id, new Dictionary<string, object?> { ["Title"] = "Initial" });
        await engine.UpdateAsync(table.Id, record.Id, new Dictionary<string, object?> { ["Title"] = "Updated" });
        await engine.DeleteAsync(table.Id, record.Id);

        await using var context = _fixture.CreateContext();
        var audits = await context.RecordAudits
            .Where(a => a.TableId == table.Id && a.RecordId == record.Id)
            .OrderBy(a => a.Version)
            .ToListAsync();

        Assert.Equal(3, audits.Count);
        Assert.All(audits, audit => Assert.Equal(_fixture.CurrentUserId, audit.UserId));
        Assert.Collection(audits,
            audit => Assert.Equal(ChangeType.Create, audit.ChangeType),
            audit => Assert.Equal(ChangeType.Update, audit.ChangeType),
            audit => Assert.Equal(ChangeType.Delete, audit.ChangeType));
    }

    [Fact]
    public async Task DataEngine_soft_delete_writes_audit_entry()
    {
        var table = new STable
        {
            Name = "audit_soft_delete",
            DisplayName = "Audit Soft Delete",
            SupportsSoftDelete = true,
            Fields =
            [
                new() { Name = "Title", Label = "Titre", DataType = FieldDataType.Text, IsRequired = true }
            ]
        };

        var engine = _fixture.CreateDataEngine();
        await engine.CreateTableAsync(table);

        var record = await engine.InsertAsync(table.Id, new Dictionary<string, object?> { ["Title"] = "Initial" });
        await engine.DeleteAsync(table.Id, record.Id);

        await using var context = _fixture.CreateContext();
        var audits = await context.RecordAudits
            .Where(a => a.TableId == table.Id && a.RecordId == record.Id)
            .OrderBy(a => a.Version)
            .ToListAsync();

        Assert.Equal(2, audits.Count);
        Assert.Collection(audits,
            audit => Assert.Equal(ChangeType.Create, audit.ChangeType),
            audit => Assert.Equal(ChangeType.SoftDelete, audit.ChangeType));

        var stored = await context.Records.FirstOrDefaultAsync(r => r.Id == record.Id);
        Assert.NotNull(stored);
        Assert.NotNull(stored!.DeletedAt);
    }

    private static string? ReadField(F_Record record, string fieldName)
    {
        using var doc = JsonDocument.Parse(record.DataJson);
        return doc.RootElement.TryGetProperty(fieldName, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => value.ToString()
            }
            : null;
    }
}
