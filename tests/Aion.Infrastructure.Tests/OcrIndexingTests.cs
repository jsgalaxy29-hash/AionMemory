using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Aion.Domain;
using Aion.Infrastructure;
using Aion.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aion.Infrastructure.Tests;

public sealed class OcrIndexingTests
{
    [Fact]
    public async Task Upload_image_ocr_stub_indexes_record()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;
        await using var context = new AionDbContext(options, new TestWorkspaceContext());
        await context.Database.EnsureCreatedAsync();

        var table = STable.Create("Invoices", "Invoices", new[]
        {
            SFieldDefinition.Text("title", "Titre", required: true)
        });

        context.Tables.Add(table);
        await context.SaveChangesAsync();

        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["title"] = "Test Invoice"
        });

        var record = new F_Record
        {
            TableId = table.Id,
            DataJson = payload
        };

        context.Records.Add(record);
        await context.SaveChangesAsync();

        var storageRoot = Path.Combine(Path.GetTempPath(), $"aion-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);
        var storageOptions = Options.Create(new StorageOptions
        {
            RootPath = storageRoot,
            EncryptPayloads = false,
            RequireIntegrityCheck = false
        });

        try
        {
            var storage = new StorageService(storageOptions, NullLogger<StorageService>.Instance);
            var recordSearch = new RecordSearchIndexService(context, NullLogger<RecordSearchIndexService>.Instance);
            var provider = new ServiceCollection().BuildServiceProvider();
            var search = new SemanticSearchService(context, NullLogger<SemanticSearchService>.Instance, provider, recordSearch);
            var fileStorage = new FileStorageService(storageOptions, context, search, storage, NullLogger<FileStorageService>.Instance);
            var visionService = new AionVisionService(
                context,
                new StubVisionService("invoice-ocr"),
                search,
                recordSearch,
                NullLogger<AionVisionService>.Instance);

            await using (var content = new MemoryStream(new byte[] { 1, 2, 3, 4 }))
            {
                var file = await fileStorage.SaveAsync("invoice.png", content, "image/png");
                await fileStorage.LinkAsync(file.Id, table.Name, record.Id);
                await visionService.AnalyzeAsync(new VisionAnalysisRequest(file.Id, VisionAnalysisType.Ocr));
            }

            var results = (await search.SearchAsync("invoice-ocr")).ToList();

            Assert.Contains(results, hit => hit.TargetType == "Record" && hit.TargetId == record.Id);
        }
        finally
        {
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }

    private sealed class StubVisionService : IVisionService
    {
        private readonly string _ocrText;

        public StubVisionService(string ocrText)
        {
            _ocrText = ocrText;
        }

        public Task<S_VisionAnalysis> AnalyzeAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            var payload = JsonSerializer.Serialize(new
            {
                request.FileId,
                request.AnalysisType,
                ocrText = _ocrText
            });

            return Task.FromResult(new S_VisionAnalysis
            {
                FileId = request.FileId,
                AnalysisType = request.AnalysisType,
                ResultJson = payload
            });
        }
    }
}
