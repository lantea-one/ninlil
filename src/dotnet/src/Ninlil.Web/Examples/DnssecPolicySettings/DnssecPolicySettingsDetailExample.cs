using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnssecPolicySettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DnssecPolicySettingsDetail" />, referenced by
///     <see cref="Controllers.DnssecPolicySettingsController.Get" />.
/// </summary>
public class DnssecPolicySettingsDetailExample : IExamplesProvider<DnssecPolicySettingsDetail>
{
    /// <summary>
    ///     This method returns the exact restriction the <c>FIPS</c> preset itself applies (see
    ///     <see cref="Ninlil.Dns.Server.Presets.DnsServerPresetCatalog.Fips" />), a realistic
    ///     compliance-driven posture rather than an arbitrary value.
    /// </summary>
    public DnssecPolicySettingsDetail GetExamples() =>
        new([
            DnssecAlgorithmEnum.RsaSha256,
            DnssecAlgorithmEnum.EcdsaP256Sha256,
            DnssecAlgorithmEnum.EcdsaP384Sha384,
        ]);
}
