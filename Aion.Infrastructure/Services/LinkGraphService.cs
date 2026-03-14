using Aion.Domain;
using Microsoft.EntityFrameworkCore;

namespace Aion.Infrastructure.Services;

public sealed class LinkGraphService : ILinkGraphService
{
    private const string DefaultNodeType = "record";
    private readonly AionDbContext _db;
    private readonly ICurrentUserService _currentUserService;

    public LinkGraphService(AionDbContext db, ICurrentUserService currentUserService)
    {
        _db = db;
        _currentUserService = currentUserService;
    }

    public async Task<S_Link> CreateLinkAsync(LinkCreateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SourceId == Guid.Empty)
        {
            throw new ArgumentException("SourceId is required.", nameof(request));
        }

        if (request.TargetId == Guid.Empty)
        {
            throw new ArgumentException("TargetId is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Type))
        {
            throw new ArgumentException("Type is required.", nameof(request));
        }

        var normalizedType = request.Type.Trim();
        var normalizedSourceType = string.IsNullOrWhiteSpace(request.SourceType) ? DefaultNodeType : request.SourceType.Trim();
        var normalizedTargetType = string.IsNullOrWhiteSpace(request.TargetType) ? DefaultNodeType : request.TargetType.Trim();
        var createdBy = request.CreatedBy ?? _currentUserService.GetCurrentUserId();

        var existing = await _db.Links.FirstOrDefaultAsync(
            link => link.SourceId == request.SourceId && link.TargetId == request.TargetId && link.Type == normalizedType,
            cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        var link = new S_Link
        {
            SourceId = request.SourceId,
            TargetId = request.TargetId,
            SourceType = normalizedSourceType,
            TargetType = normalizedTargetType,
            Relation = "semantic",
            Type = normalizedType,
            CreatedBy = createdBy,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim()
        };

        await _db.Links.AddAsync(link, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return link;
    }

    public async Task<IReadOnlyCollection<S_Link>> GetNeighborsAsync(Guid recordId, string? type = null, CancellationToken cancellationToken = default)
    {
        if (recordId == Guid.Empty)
        {
            throw new ArgumentException("RecordId is required.", nameof(recordId));
        }

        var query = _db.Links.AsNoTracking()
            .Where(link => link.SourceId == recordId || link.TargetId == recordId);

        if (!string.IsNullOrWhiteSpace(type))
        {
            var normalizedType = type.Trim();
            query = query.Where(link => link.Type == normalizedType);
        }

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<LinkGraphSlice> TraverseAsync(Guid recordId, int depth = 2, string? type = null, CancellationToken cancellationToken = default)
    {
        if (recordId == Guid.Empty)
        {
            throw new ArgumentException("RecordId is required.", nameof(recordId));
        }

        if (depth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "Depth must be at least 1.");
        }

        var normalizedType = string.IsNullOrWhiteSpace(type) ? null : type.Trim();
        var visited = new HashSet<Guid> { recordId };
        var frontier = new HashSet<Guid> { recordId };
        var links = new List<S_Link>();
        var seenLinks = new HashSet<Guid>();

        for (var level = 0; level < depth; level++)
        {
            if (frontier.Count == 0)
            {
                break;
            }

            var levelIds = frontier.ToArray();
            frontier.Clear();

            var query = _db.Links.AsNoTracking()
                .Where(link => levelIds.Contains(link.SourceId) || levelIds.Contains(link.TargetId));

            if (normalizedType is not null)
            {
                query = query.Where(link => link.Type == normalizedType);
            }

            var levelLinks = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var link in levelLinks)
            {
                if (seenLinks.Add(link.Id))
                {
                    links.Add(link);
                }
                if (visited.Add(link.SourceId))
                {
                    frontier.Add(link.SourceId);
                }

                if (visited.Add(link.TargetId))
                {
                    frontier.Add(link.TargetId);
                }
            }
        }

        var nodes = visited.ToList();
        return new LinkGraphSlice(recordId, nodes, links);
    }
}
