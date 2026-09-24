// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb;

/// <summary>
///     This class gives MongoDB the same "stage now, write later" behavior that Entity Framework
///     Core's change tracker gives the relational providers. MongoDB itself has no such concept
///     -- every driver call it exposes writes to the server immediately -- so the
///     <c>Add</c>/<c>Update</c>/<c>Remove</c> methods on a Mongo repository do not touch the
///     server at all; they hand this class a small function that performs the write, and this
///     class holds onto it until <see cref="MongoUnitOfWork.SaveChangesAsync" /> asks it to run
///     everything that was staged, in the order it was staged. This is registered with a scoped
///     lifetime, so all of the repositories used to serve a single request share the same list of
///     pending writes.
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
        // Take a snapshot of the pending operations and clear the list immediately, so that a
        // write staged by one operation's own side effects does not get executed twice.
        Func<CancellationToken, Task>[] operations = [.. _pendingOperations];
        _pendingOperations.Clear();

        // Perform every staged write, in the order it was staged.
        foreach (Func<CancellationToken, Task> operation in operations)
            await operation(stoppingToken);

        // We're done, report how many writes were performed.
        return operations.Length;
    }
}
