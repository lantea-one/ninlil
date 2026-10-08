// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface is an optional companion of <see cref="IUnitOfWork" />. It lets a caller
///     that changes a lot of data (a backup restore) run several
///     <see cref="IUnitOfWork.SaveChangesAsync" /> calls as one all-or-nothing operation. The
///     relational engines register an implementation; MongoDB does not yet, so callers must
///     resolve it as an optional service and handle its absence.
/// </summary>
public interface IUnitOfWorkTransactionFactory
{
    /// <summary>
    ///     This method starts a transaction that covers every save made through the current scope's
    ///     <see cref="IUnitOfWork" /> until the transaction is committed or disposed.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The started transaction.</returns>
    Task<IUnitOfWorkTransaction> BeginAsync(CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface represents one open transaction from <see cref="IUnitOfWorkTransactionFactory" />.
///     Disposing it without calling <see cref="CommitAsync" /> rolls every change back.
/// </summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    /// <summary>
    ///     This method makes every change saved since the transaction began permanent.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task CommitAsync(CancellationToken stoppingToken = default);
}
