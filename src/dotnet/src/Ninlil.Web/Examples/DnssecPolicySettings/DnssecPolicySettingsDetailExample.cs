using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnssecPolicySettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="DnssecPolicySettingsDetail" />, as
///     returned by <see cref="Controllers.DnssecPolicySettingsController.Get" />.
/// </summary>
public class DnssecPolicySettingsDetailExample : IExamplesProvider<DnssecPolicySettingsDetail>
{
    /// <summary>
    ///     This method returns the restriction the FIPS preset applies (see
    ///     <see cref="Ninlil.Dns.Server.Presets.DnsServerPresetCatalog.Fips" />): RSA/SHA-256 and
    ///     ECDSA P-256 and P-384.
    /// </summary>
    public DnssecPolicySettingsDetail GetExamples() =>
        new([
            DnssecAlgorithmEnum.RsaSha256,
            DnssecAlgorithmEnum.EcdsaP256Sha256,
            DnssecAlgorithmEnum.EcdsaP384Sha384,
        ]);
}
