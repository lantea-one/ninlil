using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Claims;

// Define our namespace.
namespace Ninlil.Dns.Identity.Authentication;

/// <summary>
///     This class implements <see cref="IAccessTokenRevalidator" /> with a process-wide cache of
///     each account's current state, so a disabled account or a changed permission takes effect on
///     an issued access token at once on this node, with no database read on the hot path.
///     <para>
///         <b>Cache.</b> One entry per account holds the account's current role and permission
///         claims, or nothing when the account is missing, disabled or locked out. An entry lives
///         for <see cref="CacheLifetime" />. <see cref="Invalidate" /> empties the cache and
///         raises a generation number, so a load that started before the flush cannot put an old
///         answer back. <see cref="CacheLifetime" /> is also the bound for a change this node was
///         not told about (a cluster peer that has not yet applied it, or an edit made in the
///         database directly).
///     </para>
///     <para>
///         <b>Scope.</b> Loading uses a scope of its own, so the repositories read the primary
///         database, as in <see cref="ApiTokenAuthenticationHandler" />: authentication runs
///         before the read-intent middleware chooses a replica for the endpoint.
///     </para>
/// </summary>
/// <param name="scopeFactory">Creates the scope a cache miss loads the account in.</param>
/// <param name="timeProvider">The clock the cache entries age by.</param>
public sealed class AccessTokenRevalidator(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider
) : IAccessTokenRevalidator
{
    /// <summary>
    ///     This field contains how long one cached answer is used before the account is read again.
    ///     It is a code constant, not a setting: it only bounds changes that did not call
    ///     <see cref="Invalidate" />, and a longer value would delay them.
    /// </summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     This constant contains the short role claim type that a token written without claim
    ///     type mapping can carry instead of <see cref="ClaimTypes.Role" />.
    /// </summary>
    private const string ShortRoleClaimType = "role";

    private readonly ConcurrentDictionary<Guid, CachedAccount> _accounts = new();

    private long _generation;

    /// <summary>
    ///     This record holds one cached answer: the claims the account currently has, or
    ///     <see langword="null" /> when the token must be rejected.
    /// </summary>
    /// <param name="Generation">The flush generation the answer was loaded in.</param>
    /// <param name="LoadedAt">The timestamp of the load, in <see cref="TimeProvider.GetTimestamp" /> ticks.</param>
    /// <param name="Claims">The account's role and permission claims, or <see langword="null" /> to reject.</param>
    private sealed record CachedAccount(long Generation, long LoadedAt, Claim[] Claims);

    /// <inheritdoc />
    public async Task<bool> RevalidateAsync(
        ClaimsPrincipal principal,
        CancellationToken stoppingToken = default
    )
    {
        string subject =
            principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // A token without a usable subject names no account.
        if (
            principal.Identity is not ClaimsIdentity identity
            || !Guid.TryParse(subject, out Guid userId)
        )
            return false;

        long generation = Interlocked.Read(ref _generation);

        if (
            !_accounts.TryGetValue(userId, out CachedAccount cached)
            || cached.Generation != generation
            || timeProvider.GetElapsedTime(cached.LoadedAt) >= CacheLifetime
        )
        {
            cached = new CachedAccount(
                generation,
                timeProvider.GetTimestamp(),
                await LoadAsync(userId, stoppingToken)
            );
            _accounts[userId] = cached;

            // A flush that ran while the account was loading may have missed this entry.
            if (Interlocked.Read(ref _generation) != generation)
                _accounts.TryRemove(new KeyValuePair<Guid, CachedAccount>(userId, cached));
        }

        if (cached.Claims is null)
            return false;

        foreach (
            Claim claim in identity
                .FindAll(claim =>
                    claim.Type is ClaimTypes.Role or ShortRoleClaimType
                    || claim.Type == NinlilDnsClaimTypes.Permission
                )
                .ToList()
        )
            identity.RemoveClaim(claim);

        identity.AddClaims(cached.Claims);

        return true;
    }

    /// <inheritdoc />
    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        _accounts.Clear();
    }

    /// <summary>
    ///     This method reads the account and builds its current role and permission claims.
    /// </summary>
    /// <returns>The claims, or <see langword="null" /> when the account is missing, disabled or locked out (the rules <see cref="ApiTokenAuthenticationHandler" /> applies to an API token).</returns>
    private async Task<Claim[]> LoadAsync(Guid userId, CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IUserRepository userRepository =
            scope.ServiceProvider.GetRequiredService<IUserRepository>();
        IUserClaimsBuilder claimsBuilder =
            scope.ServiceProvider.GetRequiredService<IUserClaimsBuilder>();

        UserEntity user = await userRepository.GetByIdAsync(userId, stoppingToken);

        if (
            user is null
            || user.IsDisabled
            || (user.LockoutEnabled && user.LockoutEndUtc > DateTimeOffset.UtcNow)
        )
            return null;

        return
        [
            .. (await claimsBuilder.BuildClaimsAsync(user, stoppingToken)).Where(claim =>
                claim.Type == ClaimTypes.Role || claim.Type == NinlilDnsClaimTypes.Permission
            ),
        ];
    }
}
