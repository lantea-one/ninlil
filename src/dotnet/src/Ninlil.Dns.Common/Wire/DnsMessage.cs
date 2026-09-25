using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class represents a complete DNS message (the header, question, answer, authority and
///     additional sections of RFC 1035 §4) in the same form whatever transport carried it. UDP,
///     TCP, TLS, HTTPS, QUIC and DNSCrypt all carry this message inside their own framing.
///     <see cref="DnsMessageReader" /> and <see cref="DnsMessageWriter" /> convert it to and from
///     wire bytes.
/// </summary>
public class DnsMessage
{
    /// <summary>
    ///     This property contains the message identifier. The client chooses it for a query and the
    ///     server copies it into the response, so the client can match the two.
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
    ///     This property indicates whether this message was truncated to fit the transport's size
    ///     limit, so the client should retry over TCP for the full answer. This is the header's TC
    ///     bit.
    /// </summary>
    public bool IsTruncated { get; set; }

    /// <summary>
    ///     This property indicates whether the client wants the server to resolve the query
    ///     recursively if it does not have the answer. This is the header's RD bit.
    /// </summary>
    public bool RecursionDesired { get; set; }

    /// <summary>
    ///     This property indicates whether the responding server offers recursion. This is the
    ///     header's RA bit, meaningful only on a response.
    /// </summary>
    public bool RecursionAvailable { get; set; }

    /// <summary>
    ///     This property indicates whether the server has verified every record in the answer and
    ///     authority sections with DNSSEC (RFC 4035 §3.2.3). This is the header's AD bit.
    /// </summary>
    public bool AuthenticData { get; set; }

    /// <summary>
    ///     This property indicates whether the client asks a validating resolver to skip DNSSEC
    ///     validation and return the answer anyway (RFC 4035 §3.2.2). This is the header's CD bit.
    /// </summary>
    public bool CheckingDisabled { get; set; }

    /// <summary>
    ///     This property contains this message's response code. For a query it is normally
    ///     <see cref="DnsResponseCodeEnum.NoError" />; for a response it says what happened. When
    ///     <see cref="Edns" /> is present it can hold an extended 12-bit value (RFC 6891), not only
    ///     the 4-bit range of the RFC 1035 header.
    /// </summary>
    public DnsResponseCodeEnum ResponseCode { get; set; }

    /// <summary>
    ///     This property contains the Question section: what is being asked. Almost every message
    ///     has one question, although the wire format allows more.
    /// </summary>
    public List<DnsQuestion> Questions { get; init; } = [];

    /// <summary>
    ///     This property contains the Answer section: the records that directly answer the
    ///     question, present only on a response.
    /// </summary>
    public List<DnsResourceRecord> Answers { get; init; } = [];

    /// <summary>
    ///     This property contains the Authority section: records (typically NS or SOA) that name the
    ///     servers authoritative for the answer, or, on a negative answer, the SOA record a resolver
    ///     uses to decide how long to cache the non-existence.
    /// </summary>
    public List<DnsResourceRecord> AuthorityRecords { get; init; } = [];

    /// <summary>
    ///     This property contains the Additional section: records the responder adds to save the
    ///     client a follow-up query, most often the addresses of name servers in the Authority
    ///     section. The OPT and TSIG pseudo-records are not kept here; they are decoded into
    ///     <see cref="Edns" /> and <see cref="Tsig" />.
    /// </summary>
    public List<DnsResourceRecord> AdditionalRecords { get; init; } = [];

    /// <summary>
    ///     This property contains this message's EDNS(0) extension, decoded from the OPT
    ///     pseudo-record in the Additional section, or <see langword="null" /> if the sender does not
    ///     use EDNS(0).
    /// </summary>
    public DnsEdnsOptions Edns { get; set; }

    /// <summary>
    ///     This property contains this message's TSIG authentication, decoded from the TSIG
    ///     pseudo-record in the Additional section, or <see langword="null" /> if the message is not
    ///     TSIG-signed.
    /// </summary>
    public DnsTsigOptions Tsig { get; set; }
}
