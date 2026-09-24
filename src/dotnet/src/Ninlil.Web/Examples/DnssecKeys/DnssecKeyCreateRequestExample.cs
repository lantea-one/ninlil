using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnssecKeys;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DnssecKeyCreateRequest" />, referenced by
///     <see cref="Controllers.DnssecKeysController.Create" />.
/// </summary>
public class DnssecKeyCreateRequestExample : IMultipleExamplesProvider<DnssecKeyCreateRequest>
{
    /// <summary>
    ///     This method returns two genuinely different key requests -- a key-signing key using
    ///     ECDSA (whose own <see cref="DnssecKeyCreateRequest.KeySizeBits" /> is ignored, since it
    ///     is meaningful only for the RSA algorithm family) and a zone-signing key using RSA
    ///     (where it is not) -- so a caller reading this example sees both a real value and a real
    ///     zero for that same field, rather than guessing when it applies.
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
