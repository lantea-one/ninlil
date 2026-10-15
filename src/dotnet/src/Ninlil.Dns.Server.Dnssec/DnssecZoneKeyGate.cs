using System.Collections.Concurrent;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec;

/// <summary>
///     This class serialises key-state changes of one zone inside this process. A caller enters
///     the gate for a zone id, reads the zone's keys, changes them and saves, then disposes the
///     lease. <see cref="DnssecKeyManagementService.StartRolloverAsync" /> and
///     <c>DnssecKeyRolloverBackgroundService</c> share one instance (a singleton). Different
///     zones never wait for each other. An in-process gate is enough because each Ninlil process
///     has its own database and keys are not synchronised across a cluster.
/// </summary>
public sealed class DnssecZoneKeyGate
{
    /// <summary>
    ///     This field contains one semaphore per zone id. Entries are never removed: a semaphore
    ///     is small and the number of signed zones is bounded.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _semaphores = new();

    /// <summary>
    ///     This method waits until no other caller holds the gate for <paramref name="zoneId" />.
    /// </summary>
    /// <param name="zoneId">The zone whose keys the caller is about to change.</param>
    /// <param name="stoppingToken">Cancels the wait.</param>
    /// <returns>A lease; disposing it releases the gate. Disposing twice is harmless.</returns>
    public async Task<IDisposable> EnterAsync(Guid zoneId, CancellationToken stoppingToken)
    {
        SemaphoreSlim semaphore = _semaphores.GetOrAdd(zoneId, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(stoppingToken);

        return new Lease(semaphore);
    }

    /// <summary>
    ///     This class releases the zone's semaphore once, on the first dispose.
    /// </summary>
    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        /// <summary>
        ///     This field is 1 once the semaphore has been released, so a second dispose cannot
        ///     release it a second time and let two callers in.
        /// </summary>
        private int _released;

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                semaphore.Release();
        }
    }
}
