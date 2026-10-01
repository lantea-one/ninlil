using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TsigKeys;

/// <summary>
///     This class supplies the Swagger example for <see cref="TsigKeyDetail" />, as returned by
///     <see cref="Controllers.TsigKeysController.Get" />.
/// </summary>
public class TsigKeyDetailExample : IExamplesProvider<TsigKeyDetail>
{
    /// <summary>
    ///     This method returns a key without its secret; only
    ///     <see cref="TsigKeyCreateResponseExample" /> shows one.
    /// </summary>
    public TsigKeyDetail GetExamples() =>
        new(
            Guid.Parse("2b3c4d5e-6f70-4819-92a3-b4c5d6e7f809"),
            "secondary-transfer-key",
            "hmac-sha256",
            false,
            "Used by branch-office.example.com to authenticate its own zone transfer.",
            DateTimeOffset.Parse("2026-08-01T09:10:00+00:00"),
            null
        );
}
