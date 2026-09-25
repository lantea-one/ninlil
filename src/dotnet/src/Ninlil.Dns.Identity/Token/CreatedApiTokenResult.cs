using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Identity.Token;

/// <summary>
///     This record carries a newly issued API token's secret value with the
///     <see cref="ApiTokenEntity" /> it was saved as. The secret is returned only this once;
///     afterwards only its hash exists, in <see cref="Data.Abstractions.Entity.ApiTokenEntity.TokenHash" />.
/// </summary>
/// <param name="RawToken">The raw, secret token value.</param>
/// <param name="Token">The persisted token record.</param>
public record CreatedApiTokenResult(string RawToken, ApiTokenEntity Token);
