using System.Threading;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Journal;

/// <summary>
///     This class implements <see cref="IZoneWriteSerializer" /> with one
///     <see cref="SemaphoreSlim" /> per zone, created lazily on first use and keyed by zone
///     identifier in an internal dictionary guarded by a plain <see langword="lock" />. Each
///     entry is reference-counted so that once every caller
///     currently waiting on or holding a given zone's lock has finished, that zone's entry is
///     removed again -- a long-running server would otherwise accumulate one abandoned semaphore
///     per zone ever written to, for the lifetime of the process.
///     <para>
///         Registered as a singleton (see the DI registration in
///         <c>ServiceCollectionAuthoritativeExtension</c>): every caller across the whole
///         process -- every HTTP request's own scope, every background dynamic-update/transfer/
///         cloud-sync/CAA-PTR-reconciliation task -- must serialize against the exact same set of
///         per-zone locks. A per-scope or per-request instance would not serialize anything at
///         all, since two concurrent requests never share a scope.
///     </para>
/// </summary>
public sealed class ZoneWriteSerializer : IZoneWriteSerializer
{
    /// <summary>
    ///     This class is the per-zone entry this serializer tracks: the actual lock, plus how
    ///     many callers currently hold or are waiting on it, so it is safe to discard once that
    ///     count reaches zero. Keyed by zone identifier in <see cref="_entriesByZoneId" />.
    /// </summary>
    private sealed class ZoneLockEntry
    {
        /// <summary>
        ///     This property contains the actual mutual-exclusion primitive guarding this entry's
        ///     own zone's critical section. Initialized to a single available permit, so the
        ///     first caller to wait on it proceeds immediately and every later concurrent caller
        ///     queues behind it.
        /// </summary>
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        /// <summary>
        ///     This property contains how many callers currently hold or are waiting to acquire
        ///     this entry's own lock -- incremented before a caller starts waiting, decremented
        ///     once it releases. Read and written only while holding the owning
        ///     <see cref="ZoneWriteSerializer" />'s own <see cref="_gate" />.
        /// </summary>
        public int ReferenceCount { get; set; }
    }

    /// <summary>
    ///     This field contains every zone currently being waited on or held, keyed by zone
    ///     identifier. Access is always guarded by <see cref="_gate" /> -- this dictionary itself
    ///     is not thread-safe.
    /// </summary>
    private readonly Dictionary<Guid, ZoneLockEntry> _entriesByZoneId = [];

    /// <summary>
    ///     This field is the plain monitor lock guarding <see cref="_entriesByZoneId" /> itself
    ///     (creating/removing an entry, adjusting its reference count) -- a short, synchronous,
    ///     never-awaited critical section, so an ordinary <see langword="lock" /> is the right
    ///     primitive here rather than another <see cref="SemaphoreSlim" />.
    /// </summary>
    private readonly Lock _gate = new();

    /// <inheritdoc />
    public async Task<IDisposable> AcquireAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    )
    {
        ZoneLockEntry entry = RentEntry(zoneId);

        // Wait for exclusive access outside the synchronous gate above -- this can genuinely
        // suspend the calling async method for as long as another caller is already inside this
        // same zone's own critical section, which the gate itself must never block on.
        try
        {
            await entry.Semaphore.WaitAsync(stoppingToken);
        }
        catch
        {
            // The wait itself failed (most likely cancellation) -- this caller never actually
            // acquired the lock, so release its reservation on the entry before propagating,
            // or a cancelled waiter would otherwise leak a permanent reference.
            ReleaseEntry(zoneId, entry);
            throw;
        }

        return new Release(this, zoneId, entry);
    }

    /// <summary>
    ///     This method returns the <see cref="ZoneLockEntry" /> for <paramref name="zoneId" />,
    ///     creating one if this is the first currently-active caller for that zone, and records
    ///     that one more caller now depends on it existing.
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
    ///     This method records that one caller no longer depends on <paramref name="entry" />,
    ///     removing and disposing it once no caller is left waiting on or holding it -- keeping
    ///     this serializer's own memory footprint bounded by the number of zones with an
    ///     in-flight change right now, not every zone ever written to across the server's
    ///     lifetime.
    /// </summary>
    private void ReleaseEntry(Guid zoneId, ZoneLockEntry entry)
    {
        lock (_gate)
        {
            entry.ReferenceCount--;

            // Only remove the entry actually stored under this zone id right now -- a
            // just-released entry could otherwise race against, and incorrectly delete, a
            // brand-new entry a fresh caller already installed for the same zone in between.
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
    ///     This class is the disposable token <see cref="AcquireAsync" /> hands back -- releasing
    ///     the held semaphore permit and the owning serializer's own bookkeeping reference
    ///     exactly once, even if a caller disposes it more than once.
    /// </summary>
    private sealed class Release(ZoneWriteSerializer owner, Guid zoneId, ZoneLockEntry entry)
        : IDisposable
    {
        /// <summary>
        ///     This field tracks whether this token has already been disposed, so a second
        ///     <see cref="Dispose" /> call (a caller double-disposing, or disposing both
        ///     explicitly and via a <see langword="using" /> block) never releases the same
        ///     permit twice.
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
