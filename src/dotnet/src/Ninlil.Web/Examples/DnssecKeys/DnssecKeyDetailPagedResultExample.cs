using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnssecKeys;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for a page of
///     <see cref="DnssecKeyDetail" />, referenced by
///     <see cref="Controllers.DnssecKeysController.List" />.
/// </summary>
public class DnssecKeyDetailPagedResultExample : IExamplesProvider<PagedResult<DnssecKeyDetail>>
{
    /// <summary>
    ///     This method returns one page holding a zone's own KSK and ZSK together, the ordinary
    ///     RFC 6781 pairing a signed zone actually has.
    /// </summary>
    public PagedResult<DnssecKeyDetail> GetExamples() =>
        new(
            [
                new DnssecKeyDetail(
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
                new DnssecKeyDetail(
                    Guid.Parse("5e6f7081-92a3-4b4c-a5d6-e7f8091a2b3c"),
                    Guid.Parse("1a2b3c4d-5e6f-4708-9182-a3b4c5d6e7f8"),
                    DnssecKeyRoleEnum.Zsk,
                    DnssecAlgorithmEnum.EcdsaP256Sha256,
                    "MDEyMzQ1Njc4OTo7PD0+P0BBQkNERUZHSElKS0xNTk9QUVJTVFVWV1hZWltcXV5f",
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
            ],
            0,
            50,
            2
        );
}
