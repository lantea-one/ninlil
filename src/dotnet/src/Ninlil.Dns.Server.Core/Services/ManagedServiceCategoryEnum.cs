using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Services;

/// <summary>
///     This enumeration classifies every long-running component
///     <see cref="IManagedServiceRegistration" /> can describe, into the two groups the Service
///     Lifecycle Management REST surface (<c>/api/services</c>, built on top of this abstraction
///     in a later phase) presents separately: a network-facing listener a client actually
///     connects to, versus an internal maintenance loop with no listener of its own.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<ManagedServiceCategoryEnum>>]
public enum ManagedServiceCategoryEnum
{
    /// <summary>
    ///     A DNS transport listener that binds a socket (or, for DNS-over-HTTPS's own
    ///     Kestrel-mapped mode, a route) and answers real client queries -- Plain (UDP/TCP), DNS-
    ///     over-TLS, DNS-over-HTTPS, DNS-over-QUIC, DNSCrypt (UDP/TCP).
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Transport")]
    Transport,

    /// <summary>
    ///     An internal maintenance loop with no listener of its own -- DNSSEC re-signing, CAA/PTR
    ///     sync, ACME renewal, query-log cleanup, rate-limit cleanup, cloud zone sync, the
    ///     message-broker event consumer, and every other periodic or long-running task this
    ///     server runs against its own data rather than against the network.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Background")]
    Background,
}
