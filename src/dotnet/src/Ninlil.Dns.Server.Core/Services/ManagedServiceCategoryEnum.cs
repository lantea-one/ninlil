using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Services;

/// <summary>
///     This enumeration lists the two groups <c>/api/services</c> shows managed components in:
///     network listeners that clients connect to, and internal background loops.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<ManagedServiceCategoryEnum>>]
public enum ManagedServiceCategoryEnum
{
    /// <summary>
    ///     A DNS transport listener that binds a socket (or, for DNS-over-HTTPS on the web
    ///     listener, a route) and answers client queries: Plain (UDP/TCP), DNS-over-TLS,
    ///     DNS-over-HTTPS, DNS-over-QUIC and DNSCrypt (UDP/TCP).
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Transport")]
    Transport,

    /// <summary>
    ///     An internal loop with no listener, such as DNSSEC re-signing, CAA and PTR sync, ACME
    ///     renewal, query-log cleanup, cloud zone sync, the broker event consumer and the query
    ///     pipeline.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Background")]
    Background,
}
