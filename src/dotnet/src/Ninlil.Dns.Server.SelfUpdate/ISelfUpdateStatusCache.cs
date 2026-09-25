// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate;

/// <summary>
///     This interface holds the shared, in-memory <see cref="SelfUpdateStatus" />.
///     <see cref="SelfUpdateCheckService" /> writes to it from both its periodic loop and the
///     manual check, and <c>Controllers.SelfUpdateController</c> reads it, so readers see the
///     same result however the check was started.
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
