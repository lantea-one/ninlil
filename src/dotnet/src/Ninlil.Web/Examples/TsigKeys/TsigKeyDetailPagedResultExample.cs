using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TsigKeys;

/// <summary>
///     This class supplies the Swagger example for a page of <see cref="TsigKeyDetail" />, as
///     returned by <see cref="Controllers.TsigKeysController.List" />.
/// </summary>
public class TsigKeyDetailPagedResultExample : IExamplesProvider<PagedResult<TsigKeyDetail>>
{
    /// <summary>
    ///     This method returns an active key and a disabled one.
    /// </summary>
    public PagedResult<TsigKeyDetail> GetExamples() =>
        new(
            [
                new TsigKeyDetail(
                    Guid.Parse("2b3c4d5e-6f70-4819-92a3-b4c5d6e7f809"),
                    "secondary-transfer-key",
                    "hmac-sha256",
                    false,
                    "Used by branch-office.example.com to authenticate its own zone transfer.",
                    DateTimeOffset.Parse("2026-08-01T09:10:00+00:00"),
                    null
                ),
                new TsigKeyDetail(
                    Guid.Parse("3c4d5e6f-7081-492a-a3b4-c5d6e7f80912"),
                    "retired-migration-key",
                    "hmac-sha256",
                    true,
                    "No longer used since the old secondary was decommissioned.",
                    DateTimeOffset.Parse("2026-06-10T08:00:00+00:00"),
                    DateTimeOffset.Parse("2026-08-20T11:00:00+00:00")
                ),
            ],
            0,
            50,
            2
        );
}
