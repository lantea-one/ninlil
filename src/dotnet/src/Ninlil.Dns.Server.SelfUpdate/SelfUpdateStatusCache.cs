// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate;

/// <summary>
///     This class implements <see cref="ISelfUpdateStatusCache" /> as a thread-safe singleton.
///     A lock around one field is enough: writes happen once per check and reads are rare.
/// </summary>
/// <param name="currentVersionProvider">
///     The provider this cache asks for the running version, to build
///     <see cref="SelfUpdateStatus.Empty" /> before any check has completed.
/// </param>
public sealed class SelfUpdateStatusCache(ICurrentAssemblyVersionProvider currentVersionProvider)
    : ISelfUpdateStatusCache
{
    /// <summary>
    ///     This field guards <see cref="_current" />, which the periodic loop and a manual check
    ///     can write at the same time.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    ///     This field contains the recorded status, or <see langword="null" /> until the first
    ///     read or write.
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
