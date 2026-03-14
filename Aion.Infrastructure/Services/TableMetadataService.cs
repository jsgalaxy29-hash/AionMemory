using Aion.Domain;
using Microsoft.EntityFrameworkCore;

namespace Aion.Infrastructure.Services;

public sealed class TableMetadataService : ITableMetadataService
{
    private readonly AionDbContext _db;

    public TableMetadataService(AionDbContext db)
    {
        _db = db;
    }

    public async Task CreateAsync(STable table, CancellationToken cancellationToken = default)
    {
        await _db.Tables.AddAsync(table, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<STable?> GetByIdAsync(Guid tableId, CancellationToken cancellationToken = default)
        => _db.Tables
            .Include(t => t.Fields)
            .Include(t => t.Views)
            .FirstOrDefaultAsync(t => t.Id == tableId, cancellationToken);

    public async Task<IReadOnlyList<STable>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _db.Tables
            .Include(t => t.Fields)
            .Include(t => t.Views)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
