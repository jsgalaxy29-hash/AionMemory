using Aion.Domain;

namespace Aion.Infrastructure.Services;

internal sealed class NoopNoteTaggingService : INoteTaggingService
{
    public Task<IReadOnlyCollection<string>> SuggestTagsAsync(string title, string content, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());
}
