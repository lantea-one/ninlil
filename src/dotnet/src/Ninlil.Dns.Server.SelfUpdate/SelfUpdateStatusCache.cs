// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate;

/// <summary>
///     This class implements <see cref="ISelfUpdateStatusCache" /> as a simple, thread-safe,
///     in-process singleton -- a plain <see langword="lock" />-guarded field is sufficient here,
///     since a write only ever happens once per check (never more than a handful of times an
///     hour) and a read is a cheap, uncontended snapshot copy.
/// </summary>
/// <param name="currentVersionProvider">
///     The provider this cache asks for the running process's own current version, to seed
///     <see cref="SelfUpdateStatus.Empty" /> before any real check has ever completed.
/// </param>
public sealed class SelfUpdateStatusCache(ICurrentAssemblyVersionProvider currentVersionProvider)
    : ISelfUpdateStatusCache
{
    /// <summary>
    ///     This field guards <see cref="_current" /> against a concurrent read/write race between
    ///     the background loop and a manual "check now" request arriving at the same moment.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    ///     This field contains the actual, currently recorded status snapshot.
    /// </summary>
    private SelfUpdateStatus _current;

    /// <inheritdoc />
    public SelfUpdateStatus Current
    {
        get
        {
            lock (_gate)
            {
                return _current ??= SelfUpdateStatus.Empty(
                    currentVersionProvider.GetCurrentVersion()
                );
            }
        }
    }

    /// <inheritdoc />
    public void Update(SelfUpdateStatus status)
    {
        lock (_gate)
        {
            _current = status;
        }
    }
}
