using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnssecKeys;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DnssecKeyDetail" />, referenced by
///     <see cref="Controllers.DnssecKeysController.Get" /> and
///     <see cref="Controllers.DnssecKeysController.Create" />.
/// </summary>
public class DnssecKeyDetailExample : IMultipleExamplesProvider<DnssecKeyDetail>
{
    /// <summary>
    ///     This method returns the same two keys <see cref="DnssecKeyCreateRequestExample" />
    ///     requests, as this server would report them back. Neither example carries anything
    ///     shaped like private key material -- <see cref="DnssecKeyDetail.PublicKeyWireFormat" />
    ///     is exactly what it says, a <b>public</b> key, and this record has no other field a
    ///     private key could hide inside at all, per <see cref="DnssecKeyDetail" />'s own doc
    ///     comment.
    /// </summary>
    public IEnumerable<SwaggerExample<DnssecKeyDetail>> GetExamples() =>
        [
            new()
            {
                Name = "Key-signing key (ECDSA)",
                Summary = "Ninlil's recommended default algorithm for a new zone's own KSK.",
                Value = new DnssecKeyDetail(
                    Guid.Parse("4d5e6f70-8192-4a3b-b4c5-d6e7f8091a2b"),
                    Guid.Parse("1a2b3c4d-5e6f-4708-9182-a3b4c5d6e7f8"),
                    DnssecKeyRoleEnum.Ksk,
                    DnssecAlgorithmEnum.EcdsaP256Sha256,
                    "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+Pw==",
                    12345,
                    false,
                    "Key-signing key, generated when the zone was first signed.",
                    DateTimeOffset.Parse("2026-08-01T09:00:00+00:00"),
                    null,
                    DnssecKeyLifecycleStateEnum.Active,
                    DateTimeOffset.Parse("2026-08-01T09:00:00+00:00"),
                    null,
                    null
                ),
            },
            new()
            {
                Name = "Zone-signing key (RSA)",
                Summary = "An RSA-family algorithm, where KeySizeBits was actually meaningful.",
                Value = new DnssecKeyDetail(
                    Guid.Parse("5e6f7081-92a3-4b4c-a5d6-e7f8091a2b3c"),
                    Guid.Parse("1a2b3c4d-5e6f-4708-9182-a3b4c5d6e7f8"),
                    DnssecKeyRoleEnum.Zsk,
                    DnssecAlgorithmEnum.RsaSha256,
                    "AQOtVWRSw3fmA8Zw3g/vf2dJC9K6q5xJhBnCa6XV6xL/i1yFecgy0lVCB4vhTd25hoNwGCEXaVaAxHYVXTx0YHl3",
                    54321,
                    false,
                    "Zone-signing key, rolled over on its own, shorter schedule.",
                    DateTimeOffset.Parse("2026-08-01T09:00:00+00:00"),
                    null,
                    DnssecKeyLifecycleStateEnum.Active,
                    DateTimeOffset.Parse("2026-08-01T09:00:00+00:00"),
                    null,
                    null
                ),
            },
        ];
}
