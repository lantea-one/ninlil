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
        // The same claims as API-token authentication, plus a unique token identifier.
        List<Claim> claims =
        [
            .. await claimsBuilder.BuildClaimsAsync(user, stoppingToken),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        ];

        DateTime nowUtc = DateTime.UtcNow;
        DateTime expiresAtUtc = nowUtc.Add(_options.AccessTokenLifetime);

        SigningCredentials signingCredentials = new(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256
        );

        JwtSecurityToken token = new(
            _options.Issuer,
            _options.Audience,
            claims,
            nowUtc,
            expiresAtUtc,
            signingCredentials
        );

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
        // The plain-text token exists only here and in the result; only its hash is stored.
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

        return new RefreshTokenResult(rawRefreshToken, session);
    }

    /// <inheritdoc />
    public async Task<SessionEntity> ValidateRefreshTokenAsync(
        string rawRefreshToken,
        CancellationToken stoppingToken = default
    )
    {
        // A missing token (a cookie-mode refresh without a cookie) matches no session.
        if (string.IsNullOrEmpty(rawRefreshToken))
            return null;

        SessionEntity session = await sessionRepository.FindByRefreshTokenHashAsync(
            TokenHasher.Hash(rawRefreshToken),
            stoppingToken
        );

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
