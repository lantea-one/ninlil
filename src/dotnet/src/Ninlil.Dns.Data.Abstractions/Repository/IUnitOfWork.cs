// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface commits the changes staged across one or more repositories in one atomic
///     operation. Keeping it apart from the repositories lets a caller, for example, change a
///     user, add a session and revoke an old token, then save all three together. On the
///     relational engines the save is one transaction, so either all land or none do; the
///     MongoDB implementation runs the writes in order without a transaction (see
///     <c>MongoChangeTracker</c>).
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
