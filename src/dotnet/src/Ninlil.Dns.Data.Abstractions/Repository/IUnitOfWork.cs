// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface commits whatever changes were staged across one or more repositories in a
///     single, atomic operation. Splitting this out from the repositories themselves lets a
///     caller change a user, add a session, and revoke an old token, then persist all three
///     changes together as one unit -- either all of them land, or none of them do.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    ///     This method persists every change staged so far.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The number of entities that were written.</returns>
    Task<int> SaveChangesAsync(CancellationToken stoppingToken = default);
}
