using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TsigKeys;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="TsigKeyCreateResponse" />, referenced by
///     <see cref="Controllers.TsigKeysController.Create" />.
/// </summary>
public class TsigKeyCreateResponseExample : IExamplesProvider<TsigKeyCreateResponse>
{
    /// <summary>
    ///     This method returns a freshly created key alongside its own plaintext shared secret.
    ///     <see cref="TsigKeyCreateResponse.Secret" /> is an obviously-fake, base64-looking
    ///     placeholder value, not a value shaped to look like a real, genuinely-leakable shared
    ///     secret -- this response is the one and only place a real key's own secret is ever
    ///     returned, per <see cref="TsigKeyDetail" />'s own doc comment, so a documented example
    ///     must never look like it could be mistaken for one.
    /// </summary>
    public TsigKeyCreateResponse GetExamples() =>
        new(
            new TsigKeyDetail(
                Guid.Parse("2b3c4d5e-6f70-4819-92a3-b4c5d6e7f809"),
                "secondary-transfer-key",
                "hmac-sha256",
                false,
                "Used by branch-office.example.com to authenticate its own zone transfer.",
                DateTimeOffset.Parse("2026-08-01T09:10:00+00:00"),
                null
            ),
            "ZXhhbXBsZS10c2lnLXNoYXJlZC1zZWNyZXQtbm90LXJlYWw="
        );
}
