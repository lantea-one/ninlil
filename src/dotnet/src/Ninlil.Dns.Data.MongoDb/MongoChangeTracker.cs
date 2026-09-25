// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb;

/// <summary>
///     This class gives MongoDB the "stage now, write later" behavior that the Entity Framework
///     Core change tracker gives the relational providers. Every MongoDB driver call writes
///     immediately, so a Mongo repository's <c>Add</c>, <c>Update</c> and <c>Remove</c> do not
///     touch the server: they hand this class a function that performs the write, and
///     <see cref="MongoUnitOfWork.SaveChangesAsync" /> later runs everything staged, in order.
///     The writes are not wrapped in a transaction. It is registered as scoped, so all
///     repositories serving one request share the same pending writes.
/// </summary>
public class MongoChangeTracker
{
    /// <summary>
    ///     This property contains every write staged so far, waiting to be flushed.
    /// </summary>
    private readonly List<Func<CancellationToken, Task>> _pendingOperations = [];

    /// <summary>
    ///     This method stages a single write to be performed the next time
    ///     <see cref="FlushAsync" /> runs.
    /// </summary>
    /// <param name="operation">The write to perform, given the cancellation token to observe.</param>
    public void Stage(Func<CancellationToken, Task> operation) => _pendingOperations.Add(operation);

    /// <summary>
    ///     This method performs every write staged so far, in the order it was staged, and then
    ///     forgets them.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The number of writes that were performed.</returns>
    public async Task<int> FlushAsync(CancellationToken stoppingToken = default)
    {
        // Snapshot and clear the list first, so a write staged while flushing is not run twice.
        Func<CancellationToken, Task>[] operations = [.. _pendingOperations];
        _pendingOperations.Clear();

        foreach (Func<CancellationToken, Task> operation in operations)
            await operation(stoppingToken);

        return operations.Length;
    }
}
