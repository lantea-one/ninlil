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
}
