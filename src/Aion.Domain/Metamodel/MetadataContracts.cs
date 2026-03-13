using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Aion.Domain;

public interface ITableMetadataService
{
    Task<STable> CreateAsync(STable table, CancellationToken cancellationToken = default);
    Task<STable?> GetByIdAsync(Guid tableId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<STable>> GetAllAsync(CancellationToken cancellationToken = default);
}

public interface IFieldMetadataService
{
    Task<SFieldDefinition> AddFieldAsync(Guid tableId, SFieldDefinition field, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SFieldDefinition>> GetByTableAsync(Guid tableId, CancellationToken cancellationToken = default);
}
