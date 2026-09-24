using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using IdentityModel;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Claims;
using Ninlil.Dns.Identity.Options;

// Define our namespace.
namespace Ninlil.Dns.Identity.Token;

/// <summary>
///     This class implements <see cref="IJwtTokenService" />, issuing access tokens signed with
///     HMAC-SHA256 (RFC 7518) and opaque, cryptographically random refresh tokens tracked as
///     <see cref="SessionEntity" /> records.
/// </summary>
/// <param name="options">The signing key, issuer, audience, and lifetimes to issue tokens with.</param>
/// <param name="claimsBuilder">The service used to build a user's identity, role, and permission claims.</param>
/// <param name="sessionRepository">The repository refresh tokens are persisted through.</param>
/// <param name="unitOfWork">The unit of work session changes are committed through.</param>
public class JwtTokenService(
    IOptions<JwtOptions> options,
    IUserClaimsBuilder claimsBuilder,
    ISessionRepository sessionRepository,
    IUnitOfWork unitOfWork
) : IJwtTokenService
{
    /// <summary>
    ///     This property contains the resolved JWT settings this service was constructed with.
    /// </summary>
    private readonly JwtOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<AccessTokenResult> CreateAccessTokenAsync(
        UserEntity user,
        CancellationToken stoppingToken = default
    )
    {
        // Build the identity, role, and permission claims shared with API-token authentication,
        // plus a token identifier unique to this specific access token.
        List<Claim> claims =
        [
            .. await claimsBuilder.BuildClaimsAsync(user, stoppingToken),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        ];

        // Compute when this token expires.
        DateTime nowUtc = DateTime.UtcNow;
        DateTime expiresAtUtc = nowUtc.Add(_options.AccessTokenLifetime);

        // Build the signing credentials from the configured shared secret.
        SigningCredentials signingCredentials = new(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256
        );

        // Build and sign the token.
        JwtSecurityToken token = new(
            _options.Issuer,
            _options.Audience,
            claims,
            nowUtc,
            expiresAtUtc,
            signingCredentials
        );

        // We're done, encode and return the signed token.
        return new AccessTokenResult(new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }

    /// <inheritdoc />
    public async Task<RefreshTokenResult> CreateRefreshTokenAsync(
        Guid userId,
        string ipAddress,
        string userAgent,
        bool isPersistent,
        CancellationToken stoppingToken = default
    )
    {
        // Generate the raw, secret refresh token value. This is the only moment its plain-text
        // form ever exists; from here on, only its hash is kept.
        string rawRefreshToken = CryptoRandom.CreateUniqueId(
            32,
            CryptoRandom.OutputFormat.Base64Url
        );

        SessionEntity session = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UserId = userId,
            RefreshTokenHash = TokenHasher.Hash(rawRefreshToken),
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(
                isPersistent
                    ? _options.RefreshTokenLifetime
                    : _options.BrowserSessionRefreshTokenLifetime
            ),
            IpAddress = ipAddress,
            UserAgent = userAgent,
            IsPersistent = isPersistent,
        };

        await sessionRepository.AddAsync(session, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        // We're done, return the raw token alongside the persisted session record.
        return new RefreshTokenResult(rawRefreshToken, session);
    }

    /// <inheritdoc />
    public async Task<SessionEntity> ValidateRefreshTokenAsync(
        string rawRefreshToken,
        CancellationToken stoppingToken = default
    )
    {
        // No token at all (a cookie-mode refresh from a browser holding no refresh cookie) can
        // never match a session.
        if (string.IsNullOrEmpty(rawRefreshToken))
            return null;

        SessionEntity session = await sessionRepository.FindByRefreshTokenHashAsync(
            TokenHasher.Hash(rawRefreshToken),
            stoppingToken
        );

        // The token is only valid if it matched a session, that session has not been revoked,
        // and that session has not yet expired.
        return session is { RevokedAtUtc: null } && session.ExpiresAtUtc > DateTimeOffset.UtcNow
            ? session
            : null;
    }

    /// <inheritdoc />
    public async Task RevokeRefreshTokenAsync(
        SessionEntity session,
        CancellationToken stoppingToken = default
    )
    {
        session.RevokedAtUtc = DateTimeOffset.UtcNow;
        sessionRepository.Update(session);
        await unitOfWork.SaveChangesAsync(stoppingToken);
    }
}
