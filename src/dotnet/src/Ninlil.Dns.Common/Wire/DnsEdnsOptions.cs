// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class represents the EDNS(0) extension to a DNS message, defined by RFC 6891. On the
///     wire, this information does not travel as a header field; it is carried inside a
///     pseudo-record of type OPT, tucked into the message's Additional section. A
///     <see cref="Ninlil.Dns.Common.Wire.DnsMessage" /> with no OPT record present has no
///     <see cref="DnsEdnsOptions" /> at all (its <c>Edns</c> property is <see langword="null" />),
///     since that is exactly what tells a server the requestor does not speak EDNS(0).
/// </summary>
/// <param name="Version">
///     The EDNS version the sender implements. RFC 6891 defines only version 0; any other value
///     tells the receiver the sender speaks a version it does not understand.
/// </param>
/// <param name="RequestorPayloadSize">
///     The largest UDP response, in bytes, the requestor claims it can receive without needing to
///     fall back to TCP.
/// </param>
/// <param name="DnssecOk">
///     Whether the requestor set the "DNSSEC OK" (DO) bit, asking the responder to include
///     DNSSEC signatures in its answer.
/// </param>
/// <param name="Options">The individual EDNS(0) options attached to the message, if any.</param>
public record DnsEdnsOptions(
    byte Version,
    ushort RequestorPayloadSize,
    bool DnssecOk,
    IReadOnlyList<DnsEdnsOption> Options
);
