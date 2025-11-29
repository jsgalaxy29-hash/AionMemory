using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;
using Aion.Infrastructure;
using Aion.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.Infrastructure.Tests;

public class DataEngineValidationTests
{
    [Fact]
    public async Task InsertAsync_applies_defaults_and_validates_required_fields()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;

        await using var context = new AionDbContext(options);
        await context.Database.MigrateAsync();

        var table = new STable
        {
            Name = "tasks",
            DisplayName = "Tâches",
            Fields = new List<SFieldDefinition>
            {
                new() { Name = "Title", Label = "Titre", DataType = EnumSFieldType.String, IsRequired = true },
                new() { Name = "Status", Label = "Statut", DataType = EnumSFieldType.String, DefaultValue = "todo" }
            }
        };

        var engine = new AionDataEngine(context, NullLogger<AionDataEngine>.Instance, new NullSearchService());
        await engine.CreateTableAsync(table);

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.InsertAsync(table.Id, "{}"));

        var record = await engine.InsertAsync(table.Id, "{ \"Title\": \"Test\" }");

        Assert.Contains("\"Status\":\"todo\"", record.DataJson);
        Assert.Equal(table.Id, record.EntityTypeId);
    }

    [Fact]
    public async Task QueryAsync_combines_view_filters_with_equals()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;

        await using var context = new AionDbContext(options);
        await context.Database.MigrateAsync();

        var table = new STable
        {
            Name = "notes",
            DisplayName = "Notes",
            Fields =
            {
                new SFieldDefinition { Name = "Title", Label = "Titre", DataType = EnumSFieldType.String, IsRequired = true },
                new SFieldDefinition { Name = "Category", Label = "Catégorie", DataType = EnumSFieldType.String }
            },
            Views =
            {
                new SViewDefinition
                {
                    Name = "byCategory",
                    QueryDefinition = "{ \"Category\": \"work\" }"
                }
            }
        };

        var engine = new AionDataEngine(context, NullLogger<AionDataEngine>.Instance, new NullSearchService());
        await engine.CreateTableAsync(table);

        await engine.InsertAsync(table.Id, "{ \"Title\": \"A\", \"Category\": \"work\" }");
        await engine.InsertAsync(table.Id, "{ \"Title\": \"B\", \"Category\": \"home\" }");

        var filtered = await engine.QueryAsync(table.Id, filter: "byCategory");
        Assert.Single(filtered);

        var combined = await engine.QueryAsync(table.Id, equals: new Dictionary<string, string?> { ["Category"] = "home" });
        Assert.Single(combined);
    }
}

file sealed class NullSearchService : ISearchService
{
    public Task<IEnumerable<SearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<SearchHit>>(Array.Empty<SearchHit>());

    public Task IndexNoteAsync(S_Note note, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task IndexRecordAsync(F_Record record, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task IndexFileAsync(F_File file, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RemoveAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
