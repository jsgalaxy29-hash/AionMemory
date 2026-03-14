using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Aion.Domain;

/// <summary>
/// Métadonnées du métamodèle tabulaire (STable / SFieldDefinition),
/// distinctes des contrats orientés modules historiques.
/// </summary>
public interface ITableMetadataService
{
    Task CreateAsync(STable table, CancellationToken cancellationToken = default);

    Task<STable?> GetByIdAsync(Guid tableId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<STable>> GetAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Gestion dédiée des champs des tables dynamiques du métamodèle tabulaire.
/// </summary>
public interface IFieldMetadataService
{
    Task AddFieldAsync(Guid tableId, SFieldDefinition field, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SFieldDefinition>> GetByTableAsync(Guid tableId, CancellationToken cancellationToken = default);
}
