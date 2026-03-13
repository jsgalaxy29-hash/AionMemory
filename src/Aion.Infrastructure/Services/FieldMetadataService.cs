using Aion.Domain;
using Microsoft.EntityFrameworkCore;

namespace Aion.Infrastructure.Services;

public sealed class FieldMetadataService : IFieldMetadataService
{
    private readonly AionDbContext _db;

    public FieldMetadataService(AionDbContext db)
    {
        _db = db;
    }

    public async Task<SFieldDefinition> AddFieldAsync(Guid tableId, SFieldDefinition field, CancellationToken cancellationToken = default)
    {
        var table = await _db.Tables
            .Include(t => t.Fields)
            .FirstOrDefaultAsync(t => t.Id == tableId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Table {tableId} not found");

        field.TableId = tableId;
        table.Fields.Add(field);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return field;
    }

    public async Task<IReadOnlyList<SFieldDefinition>> GetByTableAsync(Guid tableId, CancellationToken cancellationToken = default)
        => await _db.TableFields
            .Where(f => f.TableId == tableId)
            .OrderBy(f => f.Order)
            .ThenBy(f => f.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
