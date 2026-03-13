using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Aion.Domain;

/// <summary>
/// Métadonnées du métamodèle tabulaire (STable/SFieldDefinition),
/// distinctes des contrats <see cref="IMetadataService"/> orientés modules historiques (S_Module/S_EntityType).
/// </summary>
public interface ITableMetadataService
{
    Task<STable> CreateAsync(STable table, CancellationToken cancellationToken = default);
    Task<STable?> GetByIdAsync(Guid tableId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<STable>> GetAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Gestion dédiée des champs de tables dynamiques du métamodèle tabulaire.
/// </summary>
public interface IFieldMetadataService
{
    Task<SFieldDefinition> AddFieldAsync(Guid tableId, SFieldDefinition field, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SFieldDefinition>> GetByTableAsync(Guid tableId, CancellationToken cancellationToken = default);
}
