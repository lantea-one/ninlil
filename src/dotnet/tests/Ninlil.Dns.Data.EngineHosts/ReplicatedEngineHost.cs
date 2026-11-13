// Define our namespace.
namespace Ninlil.Dns.Data.EngineHosts;

/// <summary>
///     This class is the base of the hosts that run a primary and at least one replica of one
///     engine. It is an <see cref="EngineHost" /> whose <see cref="EngineHost.BuildConnectionString" />
///     points at the primary, so every test and benchmark helper that takes a plain host also works
///     on the primary of a replicated one. The replica side is reached through
///     <see cref="BuildReplicaConnectionString" />, and replication is controlled with
///     <see cref="PauseReplicationAsync" />, <see cref="ResumeReplicationAsync" /> and
///     <see cref="WaitForCatchUpAsync" />, which is what the lag, routing and read-your-own-writes
///     tests of the clustered-storage work need. The replicated hosts reuse the pinned images of
///     the single-node hosts.
/// </summary>
public abstract class ReplicatedEngineHost : EngineHost
{
    /// <summary>
    ///     This method returns a connection string for the database called
    ///     <paramref name="databaseName" /> on the replica side. It is read-only: PostgreSQL and
    ///     MySQL refuse writes on it, MongoDB addresses the whole replica set with a secondary
    ///     read preference, and SQL Server needs the <c>ApplicationIntent=ReadOnly</c> it carries.
    /// </summary>
    public abstract string BuildReplicaConnectionString(string databaseName);

    /// <summary>
    ///     This method stops the replica from applying changes, so writes on the primary stay
    ///     invisible on the replica until <see cref="ResumeReplicationAsync" />.
    /// </summary>
    /// <param name="databaseName">
    ///     The database to pause, for engines that pause per database (SQL Server); ignored by
    ///     the others.
    /// </param>
    public abstract Task PauseReplicationAsync(string databaseName = null);

    /// <summary>
    ///     This method lets the replica apply changes again.
    /// </summary>
    /// <param name="databaseName">See <see cref="PauseReplicationAsync" />.</param>
    public abstract Task ResumeReplicationAsync(string databaseName = null);

    /// <summary>
    ///     This method returns once the replica has applied everything the primary had committed
    ///     when the call started, and throws <see cref="TimeoutException" /> after
    ///     <paramref name="timeout" /> (default 60 seconds).
    /// </summary>
    /// <param name="databaseName">See <see cref="PauseReplicationAsync" />.</param>
    /// <param name="timeout">How long to wait.</param>
    public abstract Task WaitForCatchUpAsync(string databaseName = null, TimeSpan? timeout = null);

    /// <summary>
    ///     This method polls <paramref name="condition" /> every 200 ms until it is true or
    ///     <paramref name="timeout" /> passes.
    /// </summary>
    /// <param name="what">What is awaited, for the timeout message.</param>
    /// <param name="condition">The check.</param>
    /// <param name="timeout">How long to wait (default 60 seconds).</param>
    protected static async Task PollAsync(
        string what,
        Func<Task<bool>> condition,
        TimeSpan? timeout = null
    )
    {
        DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));
        while (true)
        {
            try
            {
                if (await condition())
                    return;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                // The replica can refuse connections or lack the database for a moment.
            }

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Timed out waiting for {what}.");

            await Task.Delay(200);
        }
    }

    /// <summary>
    ///     This method runs <paramref name="attempt" /> and runs it again, up to
    ///     <paramref name="maxAttempts" /> times in all, while it fails because Docker could not
    ///     bind a published host port (see <see cref="IsPortConflict" />). Any other failure
    ///     surfaces at once, and so does the last port conflict.
    /// </summary>
    /// <remarks>
    ///     A host that must know its published ports before its containers start (a MongoDB
    ///     replica set names its members by port) cannot let Docker assign them, so it reserves
    ///     numbers with a port-0 bind and releases them. Nothing holds a number between that
    ///     release and Docker's bind, and when the test process runs in a toolchain container the
    ///     check happens in another network namespace than the host bind. A collision is therefore
    ///     possible and rare, and picking new numbers cures it. Each attempt draws three fresh
    ///     numbers, so the chance that every attempt collides is the collision chance to the power
    ///     of <paramref name="maxAttempts" />: five attempts make a repeat negligible, and a
    ///     conflict that persists past them is a real problem on the machine, which the last
    ///     error should show.
    /// </remarks>
    /// <param name="attempt">
    ///     One try. It picks its ports, starts the containers and throws if the start fails. It is
    ///     given the zero-based attempt number.
    /// </param>
    /// <param name="cleanup">
    ///     Disposes what a failed <paramref name="attempt" /> created, before the next one.
    /// </param>
    /// <param name="maxAttempts">The most attempts, at least 1.</param>
    public static async Task RetryOnPortConflictAsync(
        Func<int, Task> attempt,
        Func<Task> cleanup,
        int maxAttempts = 5
    )
    {
        for (int number = 0; ; number++)
        {
            try
            {
                await attempt(number);
                return;
            }
            catch (Exception exception) when (IsPortConflict(exception) && number + 1 < maxAttempts)
            {
                await cleanup();
            }
        }
    }

    /// <summary>
    ///     This method returns true when <paramref name="exception" />, or an exception inside it,
    ///     says Docker could not bind a published host port: the daemon's
    ///     <c>failed to bind host port ...: address already in use</c>, or the older
    ///     <c>port is already allocated</c>.
    /// </summary>
    /// <param name="exception">The failure from starting a container.</param>
    public static bool IsPortConflict(Exception exception)
    {
        for (Exception current = exception; current != null; current = current.InnerException)
        {
            string message = current.Message ?? string.Empty;
            if (
                message.Contains("failed to bind host port", StringComparison.OrdinalIgnoreCase)
                || message.Contains("address already in use", StringComparison.OrdinalIgnoreCase)
                || message.Contains("port is already allocated", StringComparison.OrdinalIgnoreCase)
            )
                return true;
        }

        return false;
    }
}
