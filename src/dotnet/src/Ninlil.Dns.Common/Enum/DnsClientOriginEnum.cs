using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration says how a given <c>DnsClientEntity</c> row first came to exist -- a
///     genuinely useful UI signal ("this client showed up on its own" vs. "an operator typed this
///     in") rather than a full audit trail of every event that has ever touched the row. Ninlil's
///     own operational vocabulary, not an IANA-assigned value, mirroring
///     <see cref="RecordManagedByFeatureEnum" />'s own "who created this, not what happened to it
///     since" scope exactly.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsClientOriginEnum>>]
public enum DnsClientOriginEnum
{
    /// <summary>
    ///     An operator created this row directly, through the REST API/Angular admin SPA.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Manual")]
    Manual = 0,

    /// <summary>
    ///     This row was created automatically the first time a DHCP lease was granted for its own
    ///     <c>ClientIdentifier</c>/<c>ProtocolVersion</c> pair -- see
    ///     <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpClientRegistryAutoPopulationService</c>. An
    ///     operator is still free to edit a row with this origin exactly like a manually created
    ///     one; the origin only ever describes how the row began, never a permission or
    ///     restriction on what happens to it afterward.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Dhcp")]
    Dhcp = 1,
}
