using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Zones;

/// <summary>
///     This class supplies the Swagger examples for <see cref="ZoneDetail" />, as returned by
///     <see cref="Controllers.ZonesController.Get" /> and
///     <see cref="Controllers.ZonesController.Create" />.
/// </summary>
public class ZoneDetailExample : IMultipleExamplesProvider<ZoneDetail>
{
    /// <summary>
    ///     This method returns the two zones <see cref="ZoneWriteRequestExample" /> submits, as the
    ///     server reports them. The record examples in <see cref="Records.RecordExampleCatalog" />
    ///     belong to the first.
    /// </summary>
    public IEnumerable<SwaggerExample<ZoneDetail>> GetExamples() =>
        [
            new()
            {
                Name = "Primary, DNSSEC-signed zone",
                Summary = "A self-hosted, signed zone with CAA/PTR automation enabled.",
                Value = new ZoneDetail(
                    Guid.Parse("1a2b3c4d-5e6f-4708-9182-a3b4c5d6e7f8"),
                    "example.com",
                    ZoneTypeEnum.Primary,
                    true,
                    DnssecAlgorithmEnum.EcdsaP256Sha256,
                    DnssecDenialOfExistenceModeEnum.Nsec3,
                    10,
                    "a1b2c3d4",
                    false,
                    null,
                    null,
                    ["192.0.2.53", "192.0.2.54"],
                    "secondary-transfer-key",
                    false,
                    ["example-update-key"],
                    ["secondary-transfer-key"],
                    "Primary authoritative zone for the example.com domain.",
                    null,
                    null,
                    TimeSpan.FromMinutes(5),
                    true,
                    "letsencrypt.org",
                    true,
                    null,
                    null,
                    DateTimeOffset.Parse("2026-08-01T09:00:00+00:00"),
                    DateTimeOffset.Parse("2026-08-15T14:30:00+00:00"),
                    ZoneTransferTransportEnum.Plain,
                    null,
                    false
                ),
            },
            new()
            {
                Name = "Secondary zone",
                Summary = "An unsigned zone transferring from a remote primary over TSIG.",
                Value = new ZoneDetail(
                    Guid.Parse("9f8e7d6c-5b4a-4392-81c0-f0e1d2c3b4a5"),
                    "branch-office.example.com",
                    ZoneTypeEnum.Secondary,
                    false,
                    null,
                    DnssecDenialOfExistenceModeEnum.Nsec3,
                    10,
                    null,
                    false,
                    ["203.0.113.10", "203.0.113.11"],
                    null,
                    null,
                    "secondary-transfer-key",
                    false,
                    [],
                    [],
                    "Secondary zone transferring from the branch office's own primary server.",
                    null,
                    null,
                    TimeSpan.FromMinutes(5),
                    null,
                    null,
                    null,
                    null,
                    null,
                    DateTimeOffset.Parse("2026-08-01T09:05:00+00:00"),
                    null,
                    ZoneTransferTransportEnum.Tls,
                    null,
                    false
                ),
            },
        ];
}
