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
    ///     This property contains the Fudge value, in seconds, written into every message this
    ///     server signs: how far the verifier's clock may differ from the signing time. RFC 8945
    ///     recommends 300 seconds, which absorbs ordinary clock drift while limiting how long a
    ///     captured message can be replayed. Read on every signature, so changes apply at once.
    /// </summary>
    public ushort DefaultFudgeSeconds { get; set; } = 300;
}
