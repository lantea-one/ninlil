// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class represents the EDNS(0) extension of a DNS message, defined by RFC 6891. On the
///     wire it is not a header field but an OPT pseudo-record in the Additional section. A
///     <see cref="Ninlil.Dns.Common.Wire.DnsMessage" /> without an OPT record has a
///     <see langword="null" /> <c>Edns</c> property, which tells a server the requester does not
///     speak EDNS(0).
/// </summary>
/// <param name="Version">
///     The EDNS version the sender implements. RFC 6891 defines only version 0; any other value
///     tells the receiver the sender speaks a version it does not understand.
/// </param>
/// <param name="RequestorPayloadSize">
///     The largest UDP response, in bytes, the requester says it can receive without falling back
///     to TCP.
/// </param>
/// <param name="DnssecOk">
///     Whether the requester set the "DNSSEC OK" (DO) bit, asking for DNSSEC signatures in the
///     answer.
/// </param>
/// <param name="Options">The individual EDNS(0) options attached to the message, if any.</param>
public record DnsEdnsOptions(
    byte Version,
    ushort RequestorPayloadSize,
    bool DnssecOk,
    IReadOnlyList<DnsEdnsOption> Options
);
