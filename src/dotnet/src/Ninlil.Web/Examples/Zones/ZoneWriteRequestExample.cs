using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Zones;

/// <summary>
///     This class supplies the Swagger examples for <see cref="ZoneWriteRequest" />, the request
///     body of <see cref="Controllers.ZonesController.Create" /> and
///     <see cref="Controllers.ZonesController.Replace" />.
/// </summary>
public class ZoneWriteRequestExample : IMultipleExamplesProvider<ZoneWriteRequest>
{
    /// <summary>
    ///     This method returns the three most common zone shapes, which use different fields: a
    ///     signed <see cref="ZoneTypeEnum.Primary" /> zone with DNSSEC, NOTIFY, CAA and PTR
    ///     settings, an unsigned <see cref="ZoneTypeEnum.Secondary" /> zone that needs
    ///     <see cref="ZoneWriteRequest.PrimaryServerAddresses" /> instead, and a
    ///     <see cref="ZoneTypeEnum.Forwarder" /> zone with
    ///     <see cref="ZoneWriteRequest.ForwarderAddresses" /> and its DNSSEC validation switch.
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
                    Nsec3Iterations: 0,
                    Nsec3SaltHex: null,
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
                    PtrIpv6PrefixLength: null,
                    UpdateTsigKeyNames: ["example-update-key"],
                    TransferTsigKeyNames: ["secondary-transfer-key"]
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
                    Nsec3Iterations: 0,
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
                    PtrIpv6PrefixLength: null,
                    UpdateTsigKeyNames: null,
                    TransferTsigKeyNames: null
                ),
            },
            new()
            {
                Name = "Conditional forwarder zone",
                Summary =
                    "A private zone forwarded to its own name servers, without DNSSEC validation.",
                Value = new ZoneWriteRequest(
                    "corp.example",
                    ZoneTypeEnum.Forwarder,
                    ForwarderAddresses: ["10.0.0.53", "10.0.0.54"],
                    Comments: "Internal zone answered by the corporate name servers.",
                    ValidateDnssec: false
                ),
            },
        ];
}
