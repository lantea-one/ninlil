using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnssecKeys;

/// <summary>
///     This class supplies the Swagger examples for <see cref="DnssecKeyDetail" />, as returned by
///     <see cref="Controllers.DnssecKeysController.Get" /> and
///     <see cref="Controllers.DnssecKeysController.Create" />.
/// </summary>
public class DnssecKeyDetailExample : IMultipleExamplesProvider<DnssecKeyDetail>
{
    /// <summary>
    ///     This method returns the two keys <see cref="DnssecKeyCreateRequestExample" /> requests,
    ///     as the server reports them. <see cref="DnssecKeyDetail.PublicKeyWireFormat" /> is the
    ///     public key; the record has no field for private key material. Only the KSK has a DS
    ///     record.
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
                    null,
                    "example.com. IN DNSKEY 257 3 13 AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+Pw==",
                    "example.com. IN DS 12345 13 2 186EA634D4CF62F2671F3C757F507F8D3FE985FE431A1181B661F8EED27A59CA",
                    "186EA634D4CF62F2671F3C757F507F8D3FE985FE431A1181B661F8EED27A59CA"
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
                    null,
                    "example.com. IN DNSKEY 256 3 8 AQOtVWRSw3fmA8Zw3g/vf2dJC9K6q5xJhBnCa6XV6xL/i1yFecgy0lVCB4vhTd25hoNwGCEXaVaAxHYVXTx0YHl3",
                    null,
                    null
                ),
            },
        ];
}
