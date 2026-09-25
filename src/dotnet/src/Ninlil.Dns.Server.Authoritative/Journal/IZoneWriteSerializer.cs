// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Journal;

/// <summary>
///     This interface serializes content changes to one zone: the read-state-then-write section
///     in <see cref="ZoneUpdateService" />. Two callers changing the same zone (a REST edit, a
///     dynamic update, a cloud sync pass, a batch import) cannot interleave their reads and writes
///     of the record set and SOA serial. Different zones never block each other.
///     <para>
///         Without it, two concurrent changes can both read the same state: one may try to
///         bootstrap a zone a second time, or both may compute the same "serial plus one" and
///         leave the serial advanced once for two changes, which breaks the rule that every
///         content change increases the serial (RFC 1035).
///     </para>
/// </summary>
public interface IZoneWriteSerializer
{
    /// <summary>
    ///     This method takes the exclusive right to change a zone, waiting for any other change
    ///     to that zone to finish. Dispose the returned token to release it.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone about to be changed.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the wait.</param>
    /// <returns>
    ///     A token for exclusive access to <paramref name="zoneId" />. Release it (with
    ///     <see langword="using" />) once the caller's changes are committed.
    /// </returns>
    Task<IDisposable> AcquireAsync(Guid zoneId, CancellationToken stoppingToken = default);
}
