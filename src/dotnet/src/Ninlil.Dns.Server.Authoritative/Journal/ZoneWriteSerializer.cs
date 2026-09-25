using System.Threading;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Journal;

/// <summary>
///     This class implements <see cref="IZoneWriteSerializer" /> with one
///     <see cref="SemaphoreSlim" /> per zone, created on first use in a dictionary guarded by a
///     lock. Entries are reference-counted and removed when no caller holds or waits on them, so
///     memory stays bounded by the zones being changed right now.
///     <para>
///         It must be a singleton (see <c>ServiceCollectionAuthoritativeExtension</c>): every
///         request and background task has to share the same locks, and a scoped instance would
///         serialize nothing.
///     </para>
/// </summary>
public sealed class ZoneWriteSerializer : IZoneWriteSerializer
{
    /// <summary>
    ///     This class is one zone's entry: the lock and the number of callers holding or waiting
    ///     on it, so it can be discarded at zero.
    /// </summary>
    private sealed class ZoneLockEntry
    {
        /// <summary>
        ///     This property contains the zone's lock, a semaphore with one permit.
        /// </summary>
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        /// <summary>
        ///     This property contains how many callers hold or wait for the lock. It is only
        ///     accessed while holding <see cref="_gate" />.
        /// </summary>
        public int ReferenceCount { get; set; }
    }

    /// <summary>
    ///     This field contains the entries for zones held or waited on. Guarded by
    ///     <see cref="_gate" />.
    /// </summary>
    private readonly Dictionary<Guid, ZoneLockEntry> _entriesByZoneId = [];

    /// <summary>
    ///     This field guards <see cref="_entriesByZoneId" />. Its sections are short and never
    ///     await, so a plain lock is enough.
    /// </summary>
    private readonly Lock _gate = new();

    /// <inheritdoc />
    public async Task<IDisposable> AcquireAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    )
    {
        ZoneLockEntry entry = RentEntry(zoneId);

        // Wait outside the gate, since this can take as long as another caller holds the zone.
        try
        {
            await entry.Semaphore.WaitAsync(stoppingToken);
        }
        catch
        {
            // The wait failed (usually cancellation): drop the reference, or it would leak.
            ReleaseEntry(zoneId, entry);
            throw;
        }

        return new Release(this, zoneId, entry);
    }

    /// <summary>
    ///     This method returns the <see cref="ZoneLockEntry" /> for <paramref name="zoneId" />,
    ///     creating it if needed, and adds one reference.
    /// </summary>
    private ZoneLockEntry RentEntry(Guid zoneId)
    {
        lock (_gate)
        {
            if (!_entriesByZoneId.TryGetValue(zoneId, out ZoneLockEntry entry))
            {
                entry = new ZoneLockEntry();
                _entriesByZoneId[zoneId] = entry;
            }

            entry.ReferenceCount++;
            return entry;
        }
    }

    /// <summary>
    ///     This method drops one reference to <paramref name="entry" />, and removes and disposes
    ///     it when none are left.
    /// </summary>
    private void ReleaseEntry(Guid zoneId, ZoneLockEntry entry)
    {
        lock (_gate)
        {
            entry.ReferenceCount--;

            // Only remove it if it is still the stored entry, not a newer one for the same zone.
            if (
                entry.ReferenceCount == 0
                && _entriesByZoneId.TryGetValue(zoneId, out ZoneLockEntry current)
                && ReferenceEquals(current, entry)
            )
            {
                _entriesByZoneId.Remove(zoneId);
                entry.Semaphore.Dispose();
            }
        }
    }

    /// <summary>
    ///     This class is the token <see cref="AcquireAsync" /> returns. Disposing it releases the
    ///     permit and the reference once, even if disposed twice.
    /// </summary>
    private sealed class Release(ZoneWriteSerializer owner, Guid zoneId, ZoneLockEntry entry)
        : IDisposable
    {
        /// <summary>
        ///     This field records whether the token was disposed, so a second dispose does nothing.
        /// </summary>
        private bool _disposed;

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            entry.Semaphore.Release();
            owner.ReleaseEntry(zoneId, entry);
        }
    }
}
