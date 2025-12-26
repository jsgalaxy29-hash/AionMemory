using System.IO;
using Aion.AI;
using Aion.Domain;
using Aion.Infrastructure;
using Aion.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aion.Infrastructure.Tests;

public sealed class NoteServiceTests
{
    [Fact]
    public async Task Dictated_note_transcribes_and_links_to_target()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AionDbContext>()
            .UseSqlite(connection)
            .Options;

        var workspaceContext = new TestWorkspaceContext();
        await using var context = new AionDbContext(options, workspaceContext);
        await context.Database.MigrateAsync();

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
            var search = new NullSearchService();
            var fileStorage = new FileStorageService(storageOptions, context, search, storage, NullLogger<FileStorageService>.Instance);
            var transcriptionProvider = new StubTranscriptionProvider("Texte dicté");
            var tagger = new StubTaggingService(["rdv", "tache"]);
            var currentUser = new FixedCurrentUserService(Guid.NewGuid());
            var noteService = new NoteService(
                context,
                fileStorage,
                transcriptionProvider,
                tagger,
                search,
                currentUser,
                NullLogger<NoteService>.Instance);

            var linkTargetId = Guid.NewGuid();
            var links = new[]
            {
                new J_Note_Link
                {
                    TargetType = "Record",
                    TargetId = linkTargetId
                }
            };

            await using var audioStream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
            var note = await noteService.CreateDictatedNoteAsync("Dictée", audioStream, "note.wav", links);

            var persisted = await context.Notes
                .Include(n => n.Links)
                .SingleAsync(n => n.Id == note.Id);

            Assert.Equal("Texte dicté", persisted.Content);
            Assert.True(persisted.IsTranscribed);
            Assert.Equal(NoteSourceType.Voice, persisted.Source);
            Assert.Contains("rdv", persisted.Tags);
            Assert.Single(persisted.Links);
            Assert.Equal(linkTargetId, persisted.Links[0].TargetId);
            Assert.Equal(persisted.Id, note.Id);
        }
        finally
        {
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }

    private sealed class StubTranscriptionProvider : IAudioTranscriptionProvider
    {
        private readonly string _text;

        public StubTranscriptionProvider(string text)
        {
            _text = text;
        }

        public Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
            => Task.FromResult(new TranscriptionResult(_text, TimeSpan.FromSeconds(1), "mock"));
    }

    private sealed class StubTaggingService : INoteTaggingService
    {
        private readonly IReadOnlyCollection<string> _tags;

        public StubTaggingService(IReadOnlyCollection<string> tags)
        {
            _tags = tags;
        }

        public Task<IReadOnlyCollection<string>> SuggestTagsAsync(string title, string content, CancellationToken cancellationToken = default)
            => Task.FromResult(_tags);
    }

    private sealed class NullSearchService : ISearchService
    {
        public Task<IEnumerable<SearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<SearchHit>>(Array.Empty<SearchHit>());

        public Task IndexNoteAsync(S_Note note, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task IndexRecordAsync(F_Record record, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task IndexFileAsync(F_File file, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedCurrentUserService : ICurrentUserService
    {
        private readonly Guid _userId;

        public FixedCurrentUserService(Guid userId)
        {
            _userId = userId;
        }

        public Guid GetCurrentUserId() => _userId;
    }

    private sealed class TestWorkspaceContext : IWorkspaceContext
    {
        public Guid WorkspaceId { get; } = Guid.NewGuid();
    }
}
