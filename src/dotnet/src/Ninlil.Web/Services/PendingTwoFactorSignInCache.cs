using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

// Define our namespace.
namespace Ninlil.Web.Services;

/// <summary>
///     This class tracks sign-ins waiting for a second factor. The REST API has no cookie session,
///     so the standard ASP.NET Core Identity two-factor flow, which keeps the pending user in a
///     sign-in cookie, does not apply. Instead <c>AuthController.Login</c> hands out a short-lived
///     random token (<c>LoginResponse.MfaToken</c>), kept only in this process's memory, which
///     <c>AuthController.TwoFactorLogin</c> redeems. A restart in the middle of a sign-in means
///     signing in again, an acceptable cost for a five-minute window. Tokens are not shared
///     across cluster nodes.
/// </summary>
public sealed class PendingTwoFactorSignInCache(IMemoryCache cache)
{
    /// <summary>
    ///     This field contains how long a pending sign-in's token can be redeemed: five minutes.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     This method records a pending sign-in for a user and returns the random 32-byte token,
    ///     base64-encoded, that the caller presents with the second-factor code.
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
    ///     This field serializes <see cref="Consume" />, so the lookup and the removal behave as
    ///     one step. <see cref="IMemoryCache" /> has no atomic remove-and-return, and without
    ///     the lock two concurrent requests could both redeem one token.
    /// </summary>
    private readonly object _consumeLock = new();

    /// <summary>
    ///     This method looks up a pending sign-in's token and removes it, so it is redeemed once
    ///     whether or not the code that comes with it is correct. Of any number of concurrent
    ///     calls with the same token, at most one succeeds.
    /// </summary>
    /// <param name="token">The token to redeem.</param>
    /// <returns>The pending sign-in's user identifier, or <see langword="null" /> if the token is missing, expired or already used.</returns>
    public Guid? Consume(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        string key = CacheKey(token);

        lock (_consumeLock)
        {
            if (!cache.TryGetValue(key, out Guid userId))
                return null;

            cache.Remove(key);

            return userId;
        }
    }

    /// <summary>
    ///     This method looks up a pending sign-in's token without redeeming it. The request for an
    ///     e-mailed code uses it: the token must stay valid for the code submission that follows.
    /// </summary>
    /// <param name="token">The token to look up.</param>
    /// <returns>The pending sign-in's user identifier, or <see langword="null" /> if the token is missing, expired or already used.</returns>
    public Guid? Peek(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        return cache.TryGetValue(CacheKey(token), out Guid userId) ? userId : null;
    }

    /// <summary>
    ///     This method builds the <see cref="IMemoryCache" /> key a token is stored under.
    /// </summary>
    private static string CacheKey(string token) => $"PendingTwoFactorSignIn:{token}";
}
