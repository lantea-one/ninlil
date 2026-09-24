using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class represents a complete DNS message -- everything RFC 1035 §4 describes as
///     living in a message's header, question, answer, authority, and additional sections -- in
///     a form that is the same regardless of which transport carried it in. Plain DNS-over-UDP,
///     DNS-over-HTTPS, DNS-over-TLS, DNS-over-QUIC, and DNSCrypt all, underneath their own
///     framing, carry exactly this.
/// </summary>
public class DnsMessage
{
    /// <summary>
    ///     This property contains the message identifier a client chooses when it sends a query,
    ///     and that a server copies unchanged into its response, letting the client match a
    ///     response back to the query it was for.
    /// </summary>
    public ushort Id { get; set; }

    /// <summary>
    ///     This property indicates whether this message is a response (<see langword="true" />)
    ///     or a query (<see langword="false" />). This is the header's QR bit.
    /// </summary>
    public bool IsResponse { get; set; }

    /// <summary>
    ///     This property contains the kind of operation this message represents.
    /// </summary>
    public DnsOpcodeEnum Opcode { get; set; }

    /// <summary>
    ///     This property indicates whether a responding server is authoritative for the name in
    ///     the question section. This is the header's AA bit, meaningful only on a response.
    /// </summary>
    public bool IsAuthoritativeAnswer { get; set; }

    /// <summary>
    ///     This property indicates whether this message was truncated because it did not fit
    ///     within the transport's size limit, and the client should retry over TCP to get the
    ///     complete answer. This is the header's TC bit.
    /// </summary>
    public bool IsTruncated { get; set; }

    /// <summary>
    ///     This property indicates whether the client wants the server to pursue the query
    ///     recursively, on the client's behalf, if the server itself does not have the answer.
    ///     This is the header's RD bit.
    /// </summary>
    public bool RecursionDesired { get; set; }

    /// <summary>
    ///     This property indicates whether the responding server actually supports recursive
    ///     queries. This is the header's RA bit, meaningful only on a response.
    /// </summary>
    public bool RecursionAvailable { get; set; }

    /// <summary>
    ///     This property indicates whether every record in this response was verified by DNSSEC
    ///     validation along the entire path from the root, defined by RFC 4035 §3.2.3. This is
    ///     the header's AD bit.
    /// </summary>
    public bool AuthenticData { get; set; }

    /// <summary>
    ///     This property indicates whether the client is asking a validating resolver to skip
    ///     DNSSEC validation and return the answer regardless, defined by RFC 4035 §3.2.2. This is
    ///     the header's CD bit.
    /// </summary>
    public bool CheckingDisabled { get; set; }

    /// <summary>
    ///     This property contains this message's response code. For a query this is always
    ///     <see cref="DnsResponseCodeEnum.NoError" />; for a response it reports what happened.
    ///     When <see cref="Edns" /> is present, this can hold one of the extended, 12-bit values
    ///     RFC 6891 defines, not just the original 4-bit range RFC 1035 allocated in the header.
    /// </summary>
    public DnsResponseCodeEnum ResponseCode { get; set; }

    /// <summary>
    ///     This property contains the Question section: what is actually being asked. Almost
    ///     every message carries exactly one question, even though the wire format technically
    ///     allows for more.
    /// </summary>
    public List<DnsQuestion> Questions { get; init; } = [];

    /// <summary>
    ///     This property contains the Answer section: the records that directly answer the
    ///     question, present only on a response.
    /// </summary>
    public List<DnsResourceRecord> Answers { get; init; } = [];

    /// <summary>
    ///     This property contains the Authority section: records (typically NS or SOA) that
    ///     describe which servers are authoritative for the answer, or -- on a negative answer --
    ///     the SOA record a resolver uses to compute how long to cache the non-existence of the
    ///     name.
    /// </summary>
    public List<DnsResourceRecord> AuthorityRecords { get; init; } = [];

    /// <summary>
    ///     This property contains the Additional section: records the responder includes because
    ///     they will likely save the client a follow-up query (most commonly, the address records
    ///     for a name server returned in the Authority section), along with the EDNS(0)
    ///     pseudo-record if one is present.
    /// </summary>
    public List<DnsResourceRecord> AdditionalRecords { get; init; } = [];

    /// <summary>
    ///     This property contains this message's EDNS(0) extension, decoded from the OPT
    ///     pseudo-record in <see cref="AdditionalRecords" /> if one was present, or
    ///     <see langword="null" /> if the sender does not speak EDNS(0) at all.
    /// </summary>
    public DnsEdnsOptions Edns { get; set; }

    /// <summary>
    ///     This property contains this message's TSIG transaction authentication, decoded from
    ///     the TSIG pseudo-record in <see cref="AdditionalRecords" /> if one was present, or
    ///     <see langword="null" /> if the message was not TSIG-signed at all.
    /// </summary>
    public DnsTsigOptions Tsig { get; set; }
}
