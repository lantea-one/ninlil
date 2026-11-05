// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Ptr;

/// <summary>
///     This class holds the lock that serializes the PTR sync passes of one process.
///     <see cref="PtrSyncService" /> takes it around every pass, so the event-driven pass and
///     <c>POST /api/zones/{id}/reconcile-ptr</c>, which run on separate scopes, do not overlap.
///     It is a singleton (see <c>ServiceCollectionAuthoritativeExtension</c>) rather than a static
///     field so that each service provider owns its own lock, like each cluster node does. One lock
///     for all zones is enough: a pass only needs the database for a few commands.
///     <para>
///     The lock covers one process only. Nodes that share a database are kept consistent by the
///     duplicate removal at the end of each pass, not by this lock.
///     </para>
/// </summary>
public sealed class PtrSyncPassLock
{
    /// <summary>
    ///     This property contains the semaphore with one permit that a pass holds while it runs.
    /// </summary>
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
}
