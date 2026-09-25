using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Identity.Token;

/// <summary>
///     This record carries a newly issued refresh token's secret value with the
///     <see cref="SessionEntity" /> it belongs to. The secret is returned only this once;
///     afterwards only its hash exists, in <see cref="Data.Abstractions.Entity.SessionEntity.RefreshTokenHash" />.
/// </summary>
/// <param name="RefreshToken">The raw, secret refresh token value.</param>
/// <param name="Session">The persisted session record the refresh token belongs to.</param>
public record RefreshTokenResult(string RefreshToken, SessionEntity Session);
