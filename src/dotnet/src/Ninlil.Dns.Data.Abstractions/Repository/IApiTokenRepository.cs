using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup needed to
///     authenticate an incoming API token.
/// </summary>
public interface IApiTokenRepository : IRepository<ApiTokenEntity>
{
    /// <summary>
    ///     This method looks up an API token by the hash of its secret value.
    /// </summary>
    /// <param name="tokenHash">The hashed token secret to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching token, or <see langword="null" /> if none exists.</returns>
    Task<ApiTokenEntity> FindByTokenHashAsync(
        string tokenHash,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method marks a one-time token as consumed, but only if nobody has consumed it yet.
    ///     The check and the write are one atomic operation in the data store and take effect
    ///     immediately, without <see cref="IUnitOfWork" />. When several requests present the same
    ///     one-time token at once, exactly one call returns <see langword="true" />.
    /// </summary>
    /// <param name="tokenId">The identifier of the token to consume.</param>
    /// <param name="consumedAtUtc">The UTC time to record as the moment of consumption.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     <see langword="true" /> if this call consumed the token, or <see langword="false" /> if it
    ///     was already consumed or does not exist.
    /// </returns>
    Task<bool> TryConsumeAsync(
        Guid tokenId,
        DateTimeOffset consumedAtUtc,
        CancellationToken stoppingToken = default
    );
}
