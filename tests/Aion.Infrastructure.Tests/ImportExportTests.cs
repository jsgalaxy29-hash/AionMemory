using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;
using Aion.Infrastructure;
using Aion.Infrastructure.ModuleBuilder;
using Aion.Infrastructure.Observability;
using Aion.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aion.Infrastructure.Tests;

public sealed class ImportExportTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"aion-export-{Guid.NewGuid():N}.db");
    private readonly string _importDbPath = Path.Combine(Path.GetTempPath(), $"aion-import-{Guid.NewGuid():N}.db");
    private readonly string _exportFolder = Path.Combine(Path.GetTempPath(), $"aion-export-{Guid.NewGuid():N}");
    private readonly string _exportArchive;
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), $"aion-storage-{Guid.NewGuid():N}");
    private readonly string _importStorageRoot = Path.Combine(Path.GetTempPath(), $"aion-storage-import-{Guid.NewGuid():N}");

    public ImportExportTests()
    {
        _exportArchive = Path.Combine(_exportFolder, "export.zip");
    }

    [Fact]
    public async Task Export_then_import_restores_records_and_attachments()
    {
        Directory.CreateDirectory(_exportFolder);
        Directory.CreateDirectory(_storageRoot);
        Directory.CreateDirectory(_importStorageRoot);

        await using (var context = CreateContext(_dbPath))
        {
            await context.Database.MigrateAsync();

            var storageOptions = Options.Create(new StorageOptions
            {
                RootPath = _storageRoot,
                EncryptPayloads = false,
                RequireIntegrityCheck = true,
                EncryptionKey = Convert.ToBase64String(Encoding.UTF8.GetBytes("export-key"))
            });

            var storage = new StorageService(storageOptions, NullLogger<StorageService>.Instance);
            var search = new NullSearchService();
            var operationScopeFactory = new OperationScopeFactory();
            var dataEngine = new AionDataEngine(context, NullLogger<AionDataEngine>.Instance, search, operationScopeFactory, new NullAutomationRuleEngine(), new CurrentUserService());
            var fileStorage = new FileStorageService(storageOptions, context, search, storage, NullLogger<FileStorageService>.Instance);
            var moduleValidator = new ModuleValidator(context, NullLogger<ModuleValidator>.Instance);
            var moduleApplier = new ModuleApplier(context, moduleValidator, NullLogger<ModuleApplier>.Instance, operationScopeFactory);
            var exportService = new DataExportService(context, storage, storageOptions, NullLogger<DataExportService>.Instance);

            var table = new STable
            {
                Name = "notes",
                DisplayName = "Notes",
                Fields =
                {
                    new SFieldDefinition { Name = "Title", Label = "Title", DataType = FieldDataType.Text, IsRequired = true },
                    new SFieldDefinition { Name = "Category", Label = "Category", DataType = FieldDataType.Text }
                }
            };

            await dataEngine.CreateTableAsync(table);
            var first = await dataEngine.InsertAsync(table.Id, "{ \"Title\": \"Hello\", \"Category\": \"home\" }");
            await dataEngine.InsertAsync(table.Id, "{ \"Title\": \"Work\", \"Category\": \"work\" }");

            await using var payload = new MemoryStream(Encoding.UTF8.GetBytes("file-content"));
            var file = await fileStorage.SaveAsync("hello.txt", payload, "text/plain");
            await fileStorage.LinkAsync(file.Id, "notes", first.Id);

            var exportResult = await exportService.ExportAsync(_exportArchive, asArchive: true);
            Assert.True(File.Exists(exportResult.PackagePath));
        }

        await using (var importContext = CreateContext(_importDbPath))
        {
            await importContext.Database.MigrateAsync();

            var storageOptions = Options.Create(new StorageOptions
            {
                RootPath = _importStorageRoot,
                EncryptPayloads = false,
                RequireIntegrityCheck = true,
                EncryptionKey = Convert.ToBase64String(Encoding.UTF8.GetBytes("import-key"))
            });

            var storage = new StorageService(storageOptions, NullLogger<StorageService>.Instance);
            var search = new NullSearchService();
            var operationScopeFactory = new OperationScopeFactory();
            var dataEngine = new AionDataEngine(importContext, NullLogger<AionDataEngine>.Instance, search, operationScopeFactory, new NullAutomationRuleEngine(), new CurrentUserService());
            var fileStorage = new FileStorageService(storageOptions, importContext, search, storage, NullLogger<FileStorageService>.Instance);
            var moduleValidator = new ModuleValidator(importContext, NullLogger<ModuleValidator>.Instance);
            var moduleApplier = new ModuleApplier(importContext, moduleValidator, NullLogger<ModuleApplier>.Instance, operationScopeFactory);
            var importService = new DataImportService(importContext, moduleApplier, dataEngine, fileStorage, storage, storageOptions, NullLogger<DataImportService>.Instance);

            var result = await importService.ImportAsync(_exportArchive);

            var tables = await importContext.Tables.Include(t => t.Fields).ToListAsync();
            Assert.Single(tables);
            Assert.Equal(2, await importContext.Records.CountAsync());
            Assert.Equal(1, await importContext.Files.CountAsync());
            Assert.Equal(1, await importContext.FileLinks.CountAsync());
            Assert.Equal(2, result.RecordsInserted + result.RecordsUpdated);
            Assert.Equal(1, result.AttachmentsImported);
        }

        var storedFiles = Directory.GetFiles(_importStorageRoot, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(storedFiles);
    }

    private static AionDbContext CreateContext(string path)
    {
        var builder = new DbContextOptionsBuilder<AionDbContext>()
            .UseSqlite($"DataSource={path}");
        return new AionDbContext(builder.Options);
    }

    public ValueTask DisposeAsync()
    {
        TryDelete(_dbPath);
        TryDelete(_importDbPath);
        TryDelete(_exportFolder);
        TryDelete(_storageRoot);
        TryDelete(_importStorageRoot);
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
            // ignore cleanup errors in tests
        }
    }
}

file sealed class NullSearchService : ISearchService
{
    public Task<IEnumerable<SearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<SearchHit>>(Array.Empty<SearchHit>());
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
