using System.Threading.Tasks;
using Aion.AI;
using Aion.AI.ModuleBuilder;
using Aion.Domain.ModuleBuilder;
using Aion.Infrastructure.ModuleBuilder;
using Aion.Infrastructure.Observability;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.Infrastructure.Tests;

public class ModuleDesignServiceTests
{
    [Fact]
    public async Task DesignAndApply_creates_module_from_ai_spec()
    {
        const string payload = """
        {
          "status": "complete",
          "questions": [],
          "spec": {
            "version": "1.0",
            "slug": "inventory",
            "displayName": "Inventaire",
            "tables": [
              {
                "slug": "items",
                "displayName": "Articles",
                "fields": [
                  { "slug": "name", "label": "Nom", "dataType": "Text", "isRequired": true }
                ],
                "views": []
              }
            ]
          },
          "sources": [
            { "title": "Thing", "url": "https://schema.org/Thing", "type": "schema.org" }
          ]
        }
        """;

        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;

        await using var context = new AionDbContext(options, new TestWorkspaceContext());
        await context.Database.MigrateAsync();

        var validator = new ModuleValidator(context, NullLogger<ModuleValidator>.Instance);
        var applier = new ModuleApplier(
            context,
            validator,
            NullLogger<ModuleApplier>.Instance,
            new OperationScopeFactory(),
            new NullSecurityAuditService());

        var chatModel = new StubChatModel(payload);
        var designer = new ModuleDesignService(chatModel, validator, applier, NullLogger<ModuleDesignService>.Instance);

        var result = await designer.DesignAndApplyAsync(new ModuleDesignRequest
        {
            Prompt = "Inventaire de stock",
            UseSchemaOrg = true
        });

        Assert.True(result.Design.IsComplete);
        Assert.Single(result.Tables);
        Assert.Equal("inventory", result.Design.Spec?.Slug);
        Assert.Contains(result.Design.Sources, source => source.Url == "https://schema.org/Thing");

        var table = await context.Tables.SingleAsync(t => t.Name == "items");
        Assert.Equal("Articles", table.DisplayName);
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
