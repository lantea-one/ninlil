using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Zones;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ZoneWriteRequest" />, referenced by <see cref="Controllers.ZonesController.Create" />
///     and <see cref="Controllers.ZonesController.Replace" />.
/// </summary>
public class ZoneWriteRequestExample : IMultipleExamplesProvider<ZoneWriteRequest>
{
    /// <summary>
    ///     This method returns two genuinely different zone shapes -- a signed, self-hosted
    ///     <see cref="ZoneTypeEnum.Primary" /> zone (whose own DNSSEC/CAA/PTR-automation fields
    ///     are meaningful) and an unsigned <see cref="ZoneTypeEnum.Secondary" /> zone (whose own
    ///     <see cref="ZoneWriteRequest.PrimaryServerAddresses" /> is what it actually needs
    ///     instead) -- so a caller reading this example sees the two most common, and most
    ///     conditionally different, zone shapes in one place rather than guessing which of this
    ///     record's 20 fields apply to which zone type.
    /// </summary>
    public IEnumerable<SwaggerExample<ZoneWriteRequest>> GetExamples() =>
        [
            new()
            {
                Name = "Primary, DNSSEC-signed zone",
                Summary = "A self-hosted, signed zone with CAA/PTR automation enabled.",
                Value = new ZoneWriteRequest(
                    "example.com",
                    ZoneTypeEnum.Primary,
                    IsDnssecSigned: true,
                    DnssecAlgorithm: DnssecAlgorithmEnum.EcdsaP256Sha256,
                    DenialOfExistenceMode: DnssecDenialOfExistenceModeEnum.Nsec3,
                    Nsec3Iterations: 10,
                    Nsec3SaltHex: "a1b2c3d4",
                    IsDisabled: false,
                    PrimaryServerAddresses: null,
                    ForwarderAddresses: null,
                    NotifyAddresses: ["192.0.2.53", "192.0.2.54"],
                    TsigKeyName: "secondary-transfer-key",
                    DynamicUpdateEnabled: false,
                    Comments: "Primary authoritative zone for the example.com domain.",
                    CloudProviderCredentialId: null,
                    CloudProviderZoneId: null,
                    CloudSyncInterval: null,
                    AutoManageCaaRecords: true,
                    CaaIssuerDomain: "letsencrypt.org",
                    AutoManagePtrRecords: true,
                    PtrIpv4PrefixLength: null,
                    PtrIpv6PrefixLength: null
                ),
            },
            new()
            {
                Name = "Secondary zone",
                Summary = "An unsigned zone transferring from a remote primary over TSIG.",
                Value = new ZoneWriteRequest(
                    "branch-office.example.com",
                    ZoneTypeEnum.Secondary,
                    IsDnssecSigned: false,
                    DnssecAlgorithm: null,
                    DenialOfExistenceMode: DnssecDenialOfExistenceModeEnum.Nsec3,
                    Nsec3Iterations: 10,
                    Nsec3SaltHex: null,
                    IsDisabled: false,
                    PrimaryServerAddresses: ["203.0.113.10", "203.0.113.11"],
                    ForwarderAddresses: null,
                    NotifyAddresses: null,
                    TsigKeyName: "secondary-transfer-key",
                    DynamicUpdateEnabled: false,
                    Comments: "Secondary zone transferring from the branch office's own primary server.",
                    CloudProviderCredentialId: null,
                    CloudProviderZoneId: null,
                    CloudSyncInterval: null,
                    AutoManageCaaRecords: null,
                    CaaIssuerDomain: null,
                    AutoManagePtrRecords: null,
                    PtrIpv4PrefixLength: null,
                    PtrIpv6PrefixLength: null
                ),
            },
        ];
}
