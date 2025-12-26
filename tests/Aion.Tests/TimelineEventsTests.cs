using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aion.AI;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Aion.Infrastructure.Services;
using Aion.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aion.Tests;

public class TimelineEventsTests : IClassFixture<SqliteInMemoryFixture>
{
    private readonly SqliteInMemoryFixture _fixture;

    public TimelineEventsTests(SqliteInMemoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Timeline_records_events_for_core_operations()
    {
        var table = new STable
        {
            Name = "timeline_records",
            DisplayName = "Timeline Records",
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

        await using var noteContext = _fixture.CreateContext();
        var noteService = new NoteService(
            noteContext,
            new StubFileStorageService(),
            new StubAudioTranscriptionProvider(),
            new StubTaggingService(),
            new StubSearchService(),
            new StubCurrentUserService(Guid.NewGuid()),
            NullLogger<NoteService>.Instance);
        await noteService.CreateTextNoteAsync("Note timeline", "Contenu");

        await using var agendaContext = _fixture.CreateContext();
        var agendaService = new AionAgendaService(
            agendaContext,
            new StubNotificationService(),
            new StubCurrentUserService(Guid.NewGuid()),
            NullLogger<AionAgendaService>.Instance);
        await agendaService.AddEventAsync(new S_Event
        {
            Title = "Rendez-vous",
            Start = DateTimeOffset.UtcNow,
            Links = new List<J_Event_Link>()
        });

        var marketplaceFolder = Path.Combine(Path.GetTempPath(), $"aion-marketplace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(marketplaceFolder);
        await using var templateContext = _fixture.CreateContext();
        var module = new S_Module { Name = "Module démo", ModifiedAt = DateTimeOffset.UtcNow };
        templateContext.Modules.Add(module);
        await templateContext.SaveChangesAsync();

        var templateTimeline = new TimelineService(templateContext);
        var templateService = new TemplateService(
            templateContext,
            Options.Create(new MarketplaceOptions { MarketplaceFolder = marketplaceFolder }),
            new StubSecurityAuditService(),
            new StubCurrentUserService(Guid.NewGuid()),
            new StubModuleApplier(),
            templateTimeline);

        var package = await templateService.ExportModuleAsync(module.Id);
        await templateService.ImportModuleAsync(package);

        await using var syncContext = _fixture.CreateContext();
        var syncTimeline = new TimelineService(syncContext);
        var syncOutbox = new SyncOutboxService(syncContext, syncTimeline);
        var syncItem = new SyncItem("notes/demo.json", DateTimeOffset.UtcNow, 1, 10, "hash");
        var syncEntry = await syncOutbox.EnqueueAsync(syncItem, SyncAction.Upload);
        await syncOutbox.MarkAppliedAsync(syncEntry.Id);

        await using var timelineContext = _fixture.CreateContext();
        var timelineService = new TimelineService(timelineContext);
        var page = await timelineService.GetTimelinePageAsync(new TimelineQuery(200));
        var titles = page.Items.Select(item => item.Title).ToList();

        Assert.Contains("Enregistrement créé", titles);
        Assert.Contains("Enregistrement mis à jour", titles);
        Assert.Contains("Enregistrement supprimé", titles);
        Assert.Contains("Note créée", titles);
        Assert.Contains("Évènement planifié", titles);
        Assert.Contains("Module exporté", titles);
        Assert.Contains("Module importé", titles);
        Assert.Contains("Synchronisation en attente", titles);
        Assert.Contains("Synchronisation appliquée", titles);
    }

    private sealed class StubSearchService : ISearchService
    {
        public Task<IEnumerable<SearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<SearchHit>>(Array.Empty<SearchHit>());

        public Task IndexNoteAsync(S_Note note, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task IndexRecordAsync(F_Record record, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task IndexFileAsync(F_File file, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubFileStorageService : IFileStorageService
    {
        public Task<F_File> SaveAsync(string fileName, Stream content, string mimeType, CancellationToken cancellationToken = default)
            => Task.FromResult(new F_File { FileName = fileName, MimeType = mimeType, StoredPath = fileName });

        public Task<Stream> OpenAsync(Guid fileId, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(Guid fileId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<F_FileLink> LinkAsync(Guid fileId, string targetType, Guid targetId, string? relation = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new F_FileLink { FileId = fileId, TargetType = targetType, TargetId = targetId, Relation = relation });

        public Task<IEnumerable<F_File>> GetForAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<F_File>>(Array.Empty<F_File>());
    }

    private sealed class StubAudioTranscriptionProvider : IAudioTranscriptionProvider
    {
        public Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
            => Task.FromResult(new TranscriptionResult(string.Empty, TimeSpan.Zero));
    }

    private sealed class StubTaggingService : INoteTaggingService
    {
        public Task<IReadOnlyCollection<string>> SuggestTagsAsync(string title, string content, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());
    }

    private sealed class StubNotificationService : INotificationService
    {
        public Task ScheduleAsync(NotificationRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CancelAsync(Guid notificationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubSecurityAuditService : ISecurityAuditService
    {
        public void Track(SecurityAuditEvent auditEvent)
        {
        }

        public Task LogAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        private readonly Guid _userId;

        public StubCurrentUserService(Guid userId)
        {
            _userId = userId;
        }

        public Guid GetCurrentUserId() => _userId;
    }

    private sealed class StubModuleApplier : IModuleApplier
    {
        public Task<ChangePlan> BuildChangePlanAsync(
            ModuleSpec spec,
            ModuleSchemaState targetState = ModuleSchemaState.Draft,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChangePlan(spec.Slug, 0, targetState, Array.Empty<ModuleChange>(), false));

        public Task<IReadOnlyList<STable>> ApplyAsync(
            ModuleSpec spec,
            ModuleSchemaState targetState = ModuleSchemaState.Draft,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<STable>>(Array.Empty<STable>());

        public Task<ModuleSchemaVersion?> GetActiveVersionAsync(string moduleSlug, CancellationToken cancellationToken = default)
            => Task.FromResult<ModuleSchemaVersion?>(null);

        public Task<ModuleSchemaVersion?> GetLatestVersionAsync(string moduleSlug, CancellationToken cancellationToken = default)
            => Task.FromResult<ModuleSchemaVersion?>(null);

        public Task<ModuleSchemaVersion> PublishAsync(string moduleSlug, int version, CancellationToken cancellationToken = default)
            => Task.FromResult(new ModuleSchemaVersion
            {
                ModuleSlug = moduleSlug,
                Version = version,
                PublishedAt = DateTimeOffset.UtcNow
            });

        public Task<ModuleSchemaVersion> RollbackPublicationAsync(string moduleSlug, CancellationToken cancellationToken = default)
            => Task.FromResult(new ModuleSchemaVersion
            {
                ModuleSlug = moduleSlug,
                Version = 0,
                PublishedAt = DateTimeOffset.UtcNow
            });
    }
}
