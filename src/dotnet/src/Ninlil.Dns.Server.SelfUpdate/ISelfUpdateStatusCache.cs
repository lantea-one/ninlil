// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate;

/// <summary>
///     This interface exposes the one, shared, in-memory <see cref="SelfUpdateStatus" /> snapshot
///     both <see cref="SelfUpdateCheckService" />'s own background loop and a manual "check now"
///     REST action write into, and <c>Controllers.SelfUpdateController</c> reads from -- so both
///     ways of triggering a check are observed identically by every reader.
/// </summary>
public interface ISelfUpdateStatusCache
{
    /// <summary>
    ///     This property contains the most recently recorded status snapshot.
    /// </summary>
    SelfUpdateStatus Current { get; }

    /// <summary>
    ///     This method replaces the currently recorded status snapshot.
    /// </summary>
    /// <param name="status">The fresh snapshot to record.</param>
    void Update(SelfUpdateStatus status);
}
