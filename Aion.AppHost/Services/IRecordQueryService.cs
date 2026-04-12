using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;

namespace Aion.AppHost.Services;

/// <summary>
/// UI-friendly contract for querying and mutating records without exposing the underlying persistence layer.
/// </summary>
public interface IRecordQueryService
{
    Task<RecordPage<F_Record>> QueryAsync(Guid tableId, QuerySpec query, CancellationToken cancellationToken = default);
    Task<int> CountAsync(Guid tableId, QuerySpec query, CancellationToken cancellationToken = default);
    Task<F_Record?> GetAsync(Guid tableId, Guid recordId, CancellationToken cancellationToken = default);
    Task<ResolvedRecord?> GetResolvedAsync(Guid tableId, Guid recordId, CancellationToken cancellationToken = default);
    Task<F_Record> SaveAsync(Guid tableId, Guid? recordId, IDictionary<string, object?> data, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid tableId, Guid recordId, CancellationToken cancellationToken = default);
}
