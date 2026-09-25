using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the DNS OPCODE values assigned by IANA: the four-bit header field
///     that says what kind of operation a message performs. Nearly every message is a plain
///     <see cref="Query" />; the others cover operations such as zone updates and change
///     notifications.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsOpcodeEnum>>]
public enum DnsOpcodeEnum : byte
{
    /// <summary>
    ///     A standard query, defined by RFC 1035. This is what nearly every DNS message is.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "QUERY")]
    Query = 0,

    /// <summary>
    ///     An inverse query, defined by RFC 1035 and retired by RFC 3425. Kept so a message that uses
    ///     it is recognized and rejected rather than misread.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "IQUERY")]
    InverseQuery = 1,

    /// <summary>
    ///     A server status request, defined by RFC 1035.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "STATUS")]
    Status = 2,

    /// <summary>
    ///     A zone change notification, defined by RFC 1996. A primary sends it to tell a secondary
    ///     that a zone's serial number has increased, so the secondary does not have to poll.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "NOTIFY")]
    Notify = 4,

    /// <summary>
    ///     A dynamic update, defined by RFC 2136. Lets a client add or remove specific records
    ///     from a zone without a full zone transfer.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "UPDATE")]
    Update = 5,

    /// <summary>
    ///     A DNS Stateful Operation, defined by RFC 8490. Used over a long-lived TCP or TLS
    ///     connection for operations, such as subscribing to push notifications, that do not fit
    ///     the traditional request/response model.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DSO")]
    DnsStatefulOperation = 6,
}
