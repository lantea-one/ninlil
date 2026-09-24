// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Journal;

/// <summary>
///     This interface serializes every content-changing operation a zone goes through --
///     <see cref="ZoneUpdateService" />'s own read-current-state-then-write critical section --
///     so two concurrent callers touching the <em>same</em> zone (a REST record creation racing a
///     same-second follow-up request, an RFC 2136 dynamic update racing a cloud sync pass, two
///     concurrent record additions from a batch import, and so on) can never interleave their own
///     read and write of that zone's current record set and SOA serial. A caller touching a
///     <em>different</em> zone is never blocked by this -- only same-zone operations ever
///     contend, so unrelated zones stay fully concurrent with one another.
///     <para>
///         Without this, two concurrent changes to the same zone can each read the zone's
///         current state before the other has committed its own write, then each independently
///         decide what to do next based on that now-stale snapshot -- one call can conclude a
///         zone still has no records and attempt to bootstrap it a second time (throwing
///         inconsistent, confusing errors depending on exactly how the two calls interleaved), or
///         two concurrent record additions can each compute the same "current serial plus one"
///         value and silently clobber one another's SOA update, leaving the zone's serial
///         advanced by only one instead of two despite two real content changes having
///         happened -- a genuine RFC 1035 correctness violation, since every actual zone content
///         change must be reflected by the serial monotonically increasing.
///     </para>
/// </summary>
public interface IZoneWriteSerializer
{
    /// <summary>
    ///     This method acquires the exclusive right to change a given zone's content, waiting for
    ///     any other in-flight change to that same zone to finish first. Dispose the returned
    ///     token to release the right once the caller's own critical section is done.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone about to be changed.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the wait.</param>
    /// <returns>
    ///     A disposable token representing exclusive ownership of <paramref name="zoneId" />'s
    ///     own critical section -- release it (via <see langword="using" />) as soon as the
    ///     caller's read-decide-write sequence for that zone is fully committed.
    /// </returns>
    Task<IDisposable> AcquireAsync(Guid zoneId, CancellationToken stoppingToken = default);
}
