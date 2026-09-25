using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration says how a <c>DnsClientEntity</c> row came to exist. The UI uses it to show
///     whether a client appeared by itself or was typed in by an operator. It is not an audit
///     trail: like <see cref="RecordManagedByFeatureEnum" />, it records who created the row, not
///     what happened to it since. It is Ninlil vocabulary, not an IANA-assigned value.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsClientOriginEnum>>]
public enum DnsClientOriginEnum
{
    /// <summary>
    ///     An operator created this row through the REST API or the admin console.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Manual")]
    Manual = 0,

    /// <summary>
    ///     This row was created automatically the first time a DHCP lease was granted for its
    ///     <c>ClientIdentifier</c>/<c>ProtocolVersion</c> pair (see
    ///     <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpClientRegistryAutoPopulationService</c>). Operators
    ///     can edit it like a manual row. The origin only describes how the row began and places no
    ///     restriction on later changes.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Dhcp")]
    Dhcp = 1,
}
