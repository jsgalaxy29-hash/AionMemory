using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aion.AI;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Aion.Infrastructure.Observability;
using Aion.Infrastructure.Services.Automation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure.Services;

public sealed class FileStorageService : IFileStorageService
{
    private readonly StorageOptions _options;
    private readonly AionDbContext _db;
    private readonly ISearchService _search;
    private readonly ILogger<FileStorageService> _logger;
    private readonly IStorageService _storage;

    public FileStorageService(IOptions<StorageOptions> options, AionDbContext db, ISearchService search, IStorageService storage, ILogger<FileStorageService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _db = db;
        _search = search;
        _storage = storage;
        _logger = logger;
    }

    public async Task<F_File> SaveAsync(string fileName, Stream content, string mimeType, CancellationToken cancellationToken = default)
    {
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        if (buffer.Length > _options.MaxFileSizeBytes)
        {
            throw new InvalidOperationException($"File {fileName} exceeds the configured limit of {_options.MaxFileSizeBytes / (1024 * 1024)} MB");
        }

        await EnsureStorageQuotaAsync(buffer.Length, cancellationToken).ConfigureAwait(false);

        buffer.Position = 0;
        var id = Guid.NewGuid();
        var stored = await _storage.SaveAsync(fileName, buffer, cancellationToken).ConfigureAwait(false);

        var file = new F_File
        {
            Id = id,
            FileName = fileName,
            MimeType = mimeType,
            StoragePath = stored.Path,
            Size = stored.Size,
            Sha256 = stored.Sha256,
            UploadedAt = DateTimeOffset.UtcNow
        };

        await _db.Files.AddAsync(file, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("File {FileId} stored at {Path}", id, stored.Path);
        await _search.IndexFileAsync(file, cancellationToken).ConfigureAwait(false);
        return file;
    }

    public async Task<Stream> OpenAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await _db.Files.FirstAsync(f => f.Id == fileId, cancellationToken).ConfigureAwait(false);
        return await _storage.OpenReadAsync(file.StoragePath, _options.RequireIntegrityCheck ? file.Sha256 : null, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await _db.Files.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            _logger.LogWarning("Attempted to delete missing file {FileId}", fileId);
            return;
        }

        var links = await _db.FileLinks
            .Where(l => l.FileId == fileId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        _db.FileLinks.RemoveRange(links);
        _db.Files.Remove(file);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _storage.DeleteAsync(file.StoragePath, cancellationToken).ConfigureAwait(false);

        await _search.RemoveAsync("File", fileId, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("File {FileId} deleted with {LinkCount} link(s) removed", fileId, links.Count);
    }

    public async Task<F_FileLink> LinkAsync(Guid fileId, string targetType, Guid targetId, string? relation = null, CancellationToken cancellationToken = default)
    {
        var link = new F_FileLink
        {
            FileId = fileId,
            TargetType = targetType,
            TargetId = targetId,
            Relation = relation
        };

        await _db.FileLinks.AddAsync(link, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("File {FileId} linked to {TargetType}:{TargetId}", fileId, targetType, targetId);
        return link;
    }

    public async Task<IEnumerable<F_File>> GetForAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default)
    {
        var fileIds = await _db.FileLinks.Where(l => l.TargetType == targetType && l.TargetId == targetId)
            .Select(l => l.FileId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await _db.Files.Where(f => fileIds.Contains(f.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureStorageQuotaAsync(long incomingFileSize, CancellationToken cancellationToken)
    {
        var usedBytes = await _db.Files.SumAsync(f => f.Size, cancellationToken).ConfigureAwait(false);
        if (usedBytes + incomingFileSize > _options.MaxTotalBytes)
        {
            throw new InvalidOperationException("Storage quota exceeded; delete files before uploading new content.");
        }
    }
}

