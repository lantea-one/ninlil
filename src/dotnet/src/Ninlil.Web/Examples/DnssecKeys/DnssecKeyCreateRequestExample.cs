using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnssecKeys;

/// <summary>
///     This class supplies the Swagger examples for <see cref="DnssecKeyCreateRequest" />, the
///     request body of <see cref="Controllers.DnssecKeysController.Create" />.
/// </summary>
public class DnssecKeyCreateRequestExample : IMultipleExamplesProvider<DnssecKeyCreateRequest>
{
    /// <summary>
    ///     This method returns an ECDSA key-signing key, where
    ///     <see cref="DnssecKeyCreateRequest.KeySizeBits" /> is 0 because only RSA uses it, and a
    ///     2048-bit RSA zone-signing key, where it applies.
    /// </summary>
    public IEnumerable<SwaggerExample<DnssecKeyCreateRequest>> GetExamples() =>
        [
            new()
            {
                Name = "Key-signing key (ECDSA)",
                Summary = "Ninlil's recommended default algorithm for a new zone's own KSK.",
                Value = new DnssecKeyCreateRequest(
                    DnssecKeyRoleEnum.Ksk,
                    DnssecAlgorithmEnum.EcdsaP256Sha256,
                    0,
                    "Key-signing key, generated when the zone was first signed."
                ),
            },
            new()
            {
                Name = "Zone-signing key (RSA)",
                Summary = "An RSA-family algorithm, where KeySizeBits is actually meaningful.",
                Value = new DnssecKeyCreateRequest(
                    DnssecKeyRoleEnum.Zsk,
                    DnssecAlgorithmEnum.RsaSha256,
                    2048,
                    "Zone-signing key, rolled over on its own, shorter schedule."
                ),
            },
        ];
}
