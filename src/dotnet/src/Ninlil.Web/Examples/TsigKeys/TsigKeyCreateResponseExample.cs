using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TsigKeys;

/// <summary>
///     This class supplies the Swagger example for <see cref="TsigKeyCreateResponse" />, as
///     returned by <see cref="Controllers.TsigKeysController.Create" />.
/// </summary>
public class TsigKeyCreateResponseExample : IExamplesProvider<TsigKeyCreateResponse>
{
    /// <summary>
    ///     This method returns a new key with its plaintext secret. This response is the only place
    ///     a key's secret is returned, so the secret is a base64 placeholder that decodes to text
    ///     saying it is not real.
    /// </summary>
    public TsigKeyCreateResponse GetExamples() =>
        new(
            new TsigKeyDetail(
                Guid.Parse("2b3c4d5e-6f70-4819-92a3-b4c5d6e7f809"),
                "secondary-transfer-key",
                "hmac-sha256",
                false,
                false,
                "Used by branch-office.example.com to authenticate its own zone transfer.",
                DateTimeOffset.Parse("2026-08-01T09:10:00+00:00"),
                null
            ),
            "ZXhhbXBsZS10c2lnLXNoYXJlZC1zZWNyZXQtbm90LXJlYWw="
        );
}
