using System.Threading;
using System.Threading.Tasks;

namespace Aion.AppHost.Services;

/// <summary>
/// Coordinates the asynchronous initialization of the AppHost (database restore/migrations, demo data).
/// Consumers can await <see cref="EnsureInitializedAsync"/> to avoid blocking the UI thread.
/// </summary>
public interface IAppInitializationService
{
    /// <summary>
    /// Starts initialization if it has not run yet.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the wait, not for the initialization itself.</param>
    Task EnsureInitializedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers initialization in the background without awaiting it.
    /// </summary>
    void Warmup();
}
