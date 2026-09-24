using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

// Define our namespace.
namespace Ninlil.Web.Services;

/// <summary>
///     This class tracks a sign-in that is still waiting on a second authentication factor. This
///     project's REST API is stateless (no cookie-based session), so the standard ASP.NET Core
///     Identity two-factor flow -- which stores the "pending user" in an external sign-in cookie
///     -- does not apply; instead, a short-lived, random, opaque token stands in for that pending
///     state, held only in this process's own memory. This is deliberately not a durable store: a
///     restart mid-way through a sign-in simply requires the caller to sign in again from
///     scratch, which is an acceptable cost for a window measured in minutes.
/// </summary>
public sealed class PendingTwoFactorSignInCache(IMemoryCache cache)
{
    /// <summary>
    ///     This field contains how long a pending sign-in's own token stays redeemable before it
    ///     silently expires from the cache.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     This method records a pending sign-in for a user and returns the opaque token the
    ///     caller must present, alongside their second-factor code, to finish signing in.
    /// </summary>
    /// <param name="userId">The identifier of the user whose sign-in is pending.</param>
    /// <returns>The opaque token identifying this pending sign-in.</returns>
    public string Create(Guid userId)
    {
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        cache.Set(CacheKey(token), userId, Lifetime);

        return token;
    }

    /// <summary>
    ///     This method looks up and immediately invalidates a pending sign-in's token, so it can
    ///     never be redeemed twice.
    /// </summary>
    /// <param name="token">The opaque token to redeem.</param>
    /// <returns>The pending sign-in's own user identifier, or <see langword="null" /> if the token is missing, expired, or already used.</returns>
    public Guid? Consume(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        string key = CacheKey(token);

        if (!cache.TryGetValue(key, out Guid userId))
            return null;

        cache.Remove(key);

        return userId;
    }

    /// <summary>
    ///     This method builds the actual <see cref="IMemoryCache" /> key a token is stored under.
    /// </summary>
    private static string CacheKey(string token) => $"PendingTwoFactorSignIn:{token}";
}
