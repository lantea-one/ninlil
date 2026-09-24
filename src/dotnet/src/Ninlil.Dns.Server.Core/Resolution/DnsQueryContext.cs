using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Resolution;

/// <summary>
///     This class carries a single decoded query, and everything about how it arrived, from the
///     transport that received it through the dispatcher to whichever
///     <see cref="IDnsQueryResolver" /> ends up answering it.
/// </summary>
/// <param name="Request">The decoded query.</param>
/// <param name="Protocol">The transport the query arrived over.</param>
/// <param name="RemoteAddress">
///     The querying client's IP address. This is the address itself, not a full endpoint,
///     because the port number is meaningful only to the transport that will send the reply
///     (which already knows it from its own connection) and not to anything a resolver decides
///     based on who is asking -- an access-control list or geolocation-based answer, for example.
/// </param>
/// <param name="ReceivedAtUtc">The UTC date and time the query was received.</param>
/// <param name="RawMessage">
///     <paramref name="Request" />'s exact, not-yet-decoded wire bytes, or <see langword="null" />
///     when nothing needs them. This exists specifically for TSIG-authenticated operations (zone
///     transfers, dynamic update): per RFC 8945 §4.3.3, verifying a signature covers the literal
///     bytes a message arrived as, not a value re-serialized from <paramref name="Request" />,
///     which could legitimately differ in shape (a different, but equally valid, choice of name
///     compression, for example) from whatever was actually signed.
/// </param>
/// <param name="TsigRecordStartOffset">
///     The offset within <paramref name="RawMessage" /> a TSIG record started at, or -1 if
///     <paramref name="Request" /> carried no TSIG record (including when <paramref name="RawMessage" />
///     itself is <see langword="null" />).
/// </param>
/// <param name="ClientId">
///     The client-supplied identifier this query carried, or <see langword="null" /> when it
///     carried none -- see <see cref="Pipeline.DnsInboundMessage.ClientId" />'s own doc comment
///     for which transport can ever populate this.
/// </param>
public record DnsQueryContext(
    DnsMessage Request,
    DnsTransportProtocolEnum Protocol,
    IPAddress RemoteAddress,
    DateTimeOffset ReceivedAtUtc,
    byte[] RawMessage = null,
    int TsigRecordStartOffset = -1,
    string ClientId = null
);
