using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Aion.Infrastructure;
using Aion.Infrastructure.ModuleBuilder;
using Aion.Infrastructure.Observability;
using Aion.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aion.Infrastructure.Tests;

public sealed class TemplateServiceTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"aion-template-{Guid.NewGuid():N}.db");
    private readonly string _importDbPath = Path.Combine(Path.GetTempPath(), $"aion-template-import-{Guid.NewGuid():N}.db");
    private readonly string _marketplaceFolder = Path.Combine(Path.GetTempPath(), $"aion-template-marketplace-{Guid.NewGuid():N}");
    private readonly string _importMarketplaceFolder = Path.Combine(Path.GetTempPath(), $"aion-template-marketplace-import-{Guid.NewGuid():N}");

    [Fact]
    public async Task Export_then_import_preserves_fields_and_views()
    {
        Directory.CreateDirectory(_marketplaceFolder);
        Directory.CreateDirectory(_importMarketplaceFolder);

        TemplatePackage package;
        await using (var context = CreateContext(_dbPath))
        {
            await context.Database.MigrateAsync();

            var module = new S_Module
            {
                Name = "Tasks",
                Description = "Gestion des tâches"
            };

            var entity = new S_EntityType
            {
                Name = "task",
                PluralName = "Tasks",
                Description = "Tâches"
            };

            entity.Fields.Add(new S_Field
            {
                Name = "title",
                Label = "Titre",
                DataType = FieldDataType.Text,
                IsRequired = true,
                IsSearchable = true,
                IsListVisible = true
            });

            entity.Fields.Add(new S_Field
            {
                Name = "status",
                Label = "Statut",
                DataType = FieldDataType.Text,
                IsSearchable = true,
                IsListVisible = true
            });

            module.EntityTypes.Add(entity);
            context.Modules.Add(module);
            await context.SaveChangesAsync();

            var dataEngine = CreateDataEngine(context);
            var table = new STable
            {
                Id = entity.Id,
                Name = entity.Name,
                DisplayName = entity.PluralName,
                Description = entity.Description,
                Fields =
                {
                    new SFieldDefinition { Id = entity.Fields.First().Id, Name = "title", Label = "Titre", DataType = FieldDataType.Text, IsRequired = true },
                    new SFieldDefinition { Id = entity.Fields.Last().Id, Name = "status", Label = "Statut", DataType = FieldDataType.Text }
                },
                Views =
                {
                    new SViewDefinition
                    {
                        Name = "open",
                        DisplayName = "Ouvert",
                        QueryDefinition = "{\"status\":\"open\"}",
                        SortExpression = "title asc",
                        PageSize = 50,
                        Visualization = "table",
                        IsDefault = false
                    }
                }
            };

            await dataEngine.CreateTableAsync(table);

            var templateService = CreateTemplateService(context, _marketplaceFolder);
            package = await templateService.ExportModuleAsync(module.Id);
        }

        await using (var importContext = CreateContext(_importDbPath))
        {
            await importContext.Database.MigrateAsync();
            var templateService = CreateTemplateService(importContext, _importMarketplaceFolder);
            var imported = await templateService.ImportModuleAsync(package);

            var importedModule = await importContext.Modules
                .Include(m => m.EntityTypes).ThenInclude(e => e.Fields)
                .FirstAsync(m => m.Id == imported.Id);

            Assert.Equal("Tasks", importedModule.Name);
            Assert.Single(importedModule.EntityTypes);
            Assert.Contains(importedModule.EntityTypes.SelectMany(e => e.Fields), f => f.Name == "title");

            var importedTable = await importContext.Tables
                .Include(t => t.Fields)
                .Include(t => t.Views)
                .FirstAsync(t => t.Id == importedModule.EntityTypes.First().Id);

            Assert.Contains(importedTable.Fields, f => f.Name == "title");
            Assert.Contains(importedTable.Views, v => v.Name == "open");
        }
    }

    private static AionDbContext CreateContext(string path)
    {
        var builder = new DbContextOptionsBuilder<AionDbContext>()
            .UseSqlite($"DataSource={path}");
        return new AionDbContext(builder.Options, new TestWorkspaceContext());
    }

    private static TemplateService CreateTemplateService(AionDbContext context, string marketplaceFolder)
    {
        var marketplaceOptions = Options.Create(new MarketplaceOptions
        {
            MarketplaceFolder = marketplaceFolder
        });

        var moduleValidator = new ModuleValidator(context, NullLogger<ModuleValidator>.Instance);
        var moduleApplier = new ModuleApplier(
            context,
            moduleValidator,
            NullLogger<ModuleApplier>.Instance,
            new OperationScopeFactory(),
            new NullSecurityAuditService());

        return new TemplateService(
            context,
            marketplaceOptions,
            new NullSecurityAuditService(),
            new CurrentUserService(),
            moduleApplier);
    }

    private static AionDataEngine CreateDataEngine(AionDbContext context)
    {
        return new AionDataEngine(
            context,
            NullLogger<AionDataEngine>.Instance,
            new NullSearchService(),
            new OperationScopeFactory(),
            new NullAutomationRuleEngine(),
            new CurrentUserService());
    }

    public ValueTask DisposeAsync()
    {
        TryDelete(_dbPath);
        TryDelete(_importDbPath);
        TryDelete(_marketplaceFolder);
        TryDelete(_importMarketplaceFolder);
        return ValueTask.CompletedTask;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            else if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // ignore cleanup errors
        }
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

file sealed class NullAutomationRuleEngine : IAutomationRuleEngine
{
    public Task<IReadOnlyCollection<AutomationExecution>> ExecuteAsync(AutomationEvent automationEvent, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyCollection<AutomationExecution>>(Array.Empty<AutomationExecution>());
}
