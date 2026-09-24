// Define our namespace.
namespace Ninlil.Dns.Server.Tsig.Options;

/// <summary>
///     This class binds to the <c>DnsServer:Tsig</c> section of the application's configuration
///     and controls how <see cref="TsigService" /> signs and verifies messages.
/// </summary>
public class TsigServiceOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Tsig";

    /// <summary>
    ///     This property contains how many seconds of clock skew between signer and verifier are
    ///     tolerated, written into every message this server signs. RFC 8945 recommends 300
    ///     seconds (5 minutes) as a sensible default -- long enough to absorb ordinary clock
    ///     drift, short enough that a captured, signed message cannot be replayed indefinitely.
    /// </summary>
    public ushort DefaultFudgeSeconds { get; set; } = 300;
}
