using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb;

/// <summary>
///     This class gives MongoDB the "stage now, write later" behavior that the Entity Framework
///     Core change tracker gives the relational providers. Every MongoDB driver call writes
///     immediately, so a Mongo repository's <c>Add</c>, <c>Update</c> and <c>Remove</c> do not
///     touch the server: they hand this class a function that performs the write, and
///     <see cref="MongoUnitOfWork.SaveChangesAsync" /> later runs everything staged, in order.
///     When <see cref="MongoTransactionSupport" /> reports a replica set or sharded cluster, the
///     transactional writes run in one multi-document transaction, so a failure leaves none of
///     them behind. On a standalone server they run one by one without a transaction. A flush that
///     completes reports the entity types it wrote to <see cref="IEntityChangeNotifier" />; one
///     that throws reports nothing, even if some of its writes landed (standalone server,
///     filter-list refresh), because the caches still expire on their time-to-live. It is
///     registered as scoped, so all repositories serving one request share the same pending
///     writes.
/// </summary>
/// <param name="client">The MongoDB client used to start the transaction session.</param>
/// <param name="transactionSupport">Tells whether the deployment can run transactions.</param>
/// <param name="notifier">
///     The notifier told which entity types a flush wrote, once every write of the flush has
///     succeeded.
/// </param>
public class MongoChangeTracker(
    IMongoClient client,
    MongoTransactionSupport transactionSupport,
    IEntityChangeNotifier notifier
)
{
    /// <summary>
    ///     This property contains every write staged so far, waiting to be flushed.
    /// </summary>
    private readonly List<PendingOperation> _pendingOperations = [];

    /// <summary>
    ///     This type pairs a staged write with whether it belongs inside the transaction.
    /// </summary>
    /// <param name="Operation">The write to perform.</param>
    /// <param name="Transactional">Whether the write runs inside the transaction.</param>
    /// <param name="EntityType">The entity type the write changes, for the change signal.</param>
    private readonly record struct PendingOperation(
        Type EntityType,
        Func<IClientSessionHandle, CancellationToken, Task> Operation,
        bool Transactional
    );

    /// <summary>
    ///     This method stages a single write to be performed the next time
    ///     <see cref="FlushAsync" /> runs.
    /// </summary>
    /// <param name="entityType">
    ///     The type of the entity the write adds, changes or removes. It is reported to
    ///     <see cref="IEntityChangeNotifier" /> after the flush succeeds.
    /// </param>
    /// <param name="operation">
    ///     The write to perform, given the session of the running transaction (<see langword="null" />
    ///     when no transaction is used; see <see cref="MongoSessionWrites" />) and the cancellation
    ///     token to observe. The driver can run it more than once when it retries a transient
    ///     transaction error, so it must be safe to repeat after an aborted attempt.
    /// </param>
    /// <param name="transactional">
    ///     <see langword="true" /> to run the write inside the transaction. <see langword="false" />
    ///     runs it after the transaction has committed, for writes too large for MongoDB's
    ///     transaction limits (a transaction lives 60 seconds by default).
    /// </param>
    public void Stage(
        Type entityType,
        Func<IClientSessionHandle, CancellationToken, Task> operation,
        bool transactional = true
    ) => _pendingOperations.Add(new PendingOperation(entityType, operation, transactional));

    /// <summary>
    ///     This method performs every write staged so far, in the order it was staged, and then
    ///     forgets them. Transactional writes run first, together in one transaction when the
    ///     deployment supports it; the non-transactional writes follow in order.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The number of writes that were performed.</returns>
    public async Task<int> FlushAsync(CancellationToken stoppingToken = default)
    {
        // Snapshot and clear the list first, so a write staged while flushing is not run twice.
        PendingOperation[] operations = [.. _pendingOperations];
        _pendingOperations.Clear();

        if (operations.Length == 0)
            return 0;

        PendingOperation[] transactional = operations.Where(item => item.Transactional).ToArray();
        PendingOperation[] afterCommit = operations.Where(item => !item.Transactional).ToArray();

        if (transactional.Length > 0)
        {
            if (await transactionSupport.DetectAsync(stoppingToken))
            {
                using IClientSessionHandle session = await client.StartSessionAsync(
                    cancellationToken: stoppingToken
                );

                await session.WithTransactionAsync(
                    async (transactionSession, token) =>
                    {
                        foreach (PendingOperation item in transactional)
                            await item.Operation(transactionSession, token);

                        return true;
                    },
                    cancellationToken: stoppingToken
                );
            }
            else
            {
                foreach (PendingOperation item in transactional)
                    await item.Operation(null, stoppingToken);
            }
        }

        foreach (PendingOperation item in afterCommit)
            await item.Operation(null, stoppingToken);

        // Every write has succeeded (an exception above skips this line), so tell the caches.
        notifier.NotifyChanged([.. operations.Select(item => item.EntityType).Distinct()]);

        return operations.Length;
    }
}
