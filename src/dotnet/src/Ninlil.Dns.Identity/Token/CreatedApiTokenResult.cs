using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Identity.Token;

/// <summary>
///     This class carries a newly-issued API token's raw, secret value alongside the
///     <see cref="ApiTokenEntity" /> record it was persisted as. The raw value is returned only
///     this once; from this point on, only its hash exists anywhere, inside
///     <see cref="Data.Abstractions.Entity.ApiTokenEntity.TokenHash" />.
/// </summary>
/// <param name="RawToken">The raw, secret token value.</param>
/// <param name="Token">The persisted token record.</param>
public record CreatedApiTokenResult(string RawToken, ApiTokenEntity Token);
