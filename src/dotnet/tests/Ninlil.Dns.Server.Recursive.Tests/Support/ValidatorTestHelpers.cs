using Ninlil.Dns.Server.Recursive.Tests.Fakes;
using Ninlil.Dns.Server.Recursive.Validation;

namespace Ninlil.Dns.Server.Recursive.Tests.Support;

/// <summary>
///     This class builds the collaborators every <see cref="DnssecValidator" /> test needs.
/// </summary>
public static class ValidatorTestHelpers
{
    /// <summary>
    ///     This method builds an empty trust cache with default options.
    /// </summary>
    /// <param name="clock">The cache's clock; the system clock when omitted.</param>
    public static DnssecTrustCache CreateTrustCache(TimeProvider clock = null) =>
        new(
            new StaticOptionsMonitor<RecursiveServerOptions>(new RecursiveServerOptions()),
            new RecordingEventPublisher(),
            clock
        );
}
