using IdentityModel;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Identity.Token;

/// <summary>
///     This class implements <see cref="IApiTokenService" /> against
///     <see cref="IApiTokenRepository" />.
/// </summary>
/// <param name="apiTokenRepository">The repository API tokens are persisted through.</param>
/// <param name="unitOfWork">The unit of work token changes are committed through.</param>
public class ApiTokenService(IApiTokenRepository apiTokenRepository, IUnitOfWork unitOfWork)
    : IApiTokenService
{
    /// <inheritdoc />
    public Task<CreatedApiTokenResult> CreateStaticTokenAsync(
        Guid userId,
        string name,
        DateTimeOffset? expiresAtUtc,
        CancellationToken stoppingToken = default
    ) => CreateAsync(userId, name, ApiTokenTypeEnum.Static, expiresAtUtc, stoppingToken);

    /// <inheritdoc />
    public Task<CreatedApiTokenResult> CreateOneTimeTokenAsync(
        Guid userId,
        string name,
        DateTimeOffset? expiresAtUtc,
        CancellationToken stoppingToken = default
    ) => CreateAsync(userId, name, ApiTokenTypeEnum.OneTime, expiresAtUtc, stoppingToken);

    /// <summary>
    ///     This method generates, persists, and returns a new API token of the requested
    ///     <paramref name="type" />, shared by <see cref="CreateStaticTokenAsync" /> and
    ///     <see cref="CreateOneTimeTokenAsync" />.
    /// </summary>
    private async Task<CreatedApiTokenResult> CreateAsync(
        Guid userId,
        string name,
        ApiTokenTypeEnum type,
        DateTimeOffset? expiresAtUtc,
        CancellationToken stoppingToken
    )
    {
        // The plain-text token exists only here and in the result; only its hash is stored.
        string rawToken = CryptoRandom.CreateUniqueId(32, CryptoRandom.OutputFormat.Base64Url);

        ApiTokenEntity token = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UserId = userId,
            Name = name,
            TokenHash = TokenHasher.Hash(rawToken),
            Type = type,
            ExpiresAtUtc = expiresAtUtc,
        };

        await apiTokenRepository.AddAsync(token, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return new CreatedApiTokenResult(rawToken, token);
    }

    /// <inheritdoc />
    public async Task<ApiTokenEntity> ValidateAsync(
        string rawToken,
        CancellationToken stoppingToken = default
    )
    {
        ApiTokenEntity token = await apiTokenRepository.FindByTokenHashAsync(
            TokenHasher.Hash(rawToken),
            stoppingToken
        );

        if (token is not { RevokedAtUtc: null })
            return null;

        if (token.ExpiresAtUtc is { } expiresAtUtc && expiresAtUtc <= DateTimeOffset.UtcNow)
            return null;

        // Validation does not consume a one-time token: the caller checks the owning account
        // first and then calls ConsumeAsync, so a request rejected for account reasons leaves the
        // token usable.
        if (token.Type == ApiTokenTypeEnum.OneTime && token.ConsumedAtUtc is not null)
            return null;

        return token;
    }

    /// <inheritdoc />
    public Task<bool> ConsumeAsync(ApiTokenEntity token, CancellationToken stoppingToken = default)
    {
        if (token.Type != ApiTokenTypeEnum.OneTime)
            return Task.FromResult(true);

        // The repository makes this atomic, so two racing requests cannot both succeed.
        return apiTokenRepository.TryConsumeAsync(token.Id, DateTimeOffset.UtcNow, stoppingToken);
    }

    /// <inheritdoc />
    public async Task RevokeAsync(ApiTokenEntity token, CancellationToken stoppingToken = default)
    {
        token.RevokedAtUtc = DateTimeOffset.UtcNow;
        apiTokenRepository.Update(token);
        await unitOfWork.SaveChangesAsync(stoppingToken);
    }
}
