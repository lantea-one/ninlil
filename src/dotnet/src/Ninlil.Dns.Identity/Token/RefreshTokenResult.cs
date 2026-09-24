using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Identity.Token;

/// <summary>
///     This class carries a newly-issued refresh token's raw, secret value alongside the
///     <see cref="SessionEntity" /> record it was persisted as. The raw value is returned only
///     this once; from this point on, only its hash exists anywhere, inside
///     <see cref="Data.Abstractions.Entity.SessionEntity.RefreshTokenHash" />.
/// </summary>
/// <param name="RefreshToken">The raw, secret refresh token value.</param>
/// <param name="Session">The persisted session record the refresh token belongs to.</param>
public record RefreshTokenResult(string RefreshToken, SessionEntity Session);
