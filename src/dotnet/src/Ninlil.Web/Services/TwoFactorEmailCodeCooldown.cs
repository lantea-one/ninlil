// Define our namespace.
namespace Ninlil.Web.Services;

/// <summary>
///     This class limits how often a user can have a second-factor code e-mailed to them. Both
///     <c>AuthController.SendTwoFactorEmailCode</c> (pending sign-in) and
///     <c>TwoFactorController.SendEmailCode</c> (signed in) share one cooldown of
///     <see cref="Cooldown" /> per user, so neither endpoint can be used to mail-bomb the owner.
///     A pending sign-in may also request at most <see cref="MaxSendsPerPendingToken" /> codes
///     in all, counted for as long as the pending token lives.
///     <para>
///         A send is recorded when it is acquired, before the e-mail goes out, so two concurrent
///         requests cannot both send. A send that then fails still uses up the cooldown, which
///         keeps a broken mail server from being hammered. A refused request is not recorded.
///         The limits are constants and do not depend on the admin-login rate-limit setting. State
///         lives in this process's memory and is not shared across cluster nodes, like the
///         pending sign-ins themselves, so behind a round-robin load balancer the effective rate
///         is the per-node rate times the number of nodes.
///     </para>
/// </summary>
/// <param name="timeProvider">
///     The clock for the cooldown; <see langword="null" /> uses <see cref="TimeProvider.System" />.
///     Tests pass a fake clock.
/// </param>
public sealed class TwoFactorEmailCodeCooldown(TimeProvider timeProvider = null)
{
    /// <summary>
    ///     This field contains the minimum time between two sends to the same user: 60 seconds.
    /// </summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     This constant contains how many codes one pending sign-in may request.
    /// </summary>
    public const int MaxSendsPerPendingToken = 3;

    /// <summary>
    ///     This field contains how long a pending token's send count is kept: 5 minutes, the
    ///     lifetime of the pending sign-in itself.
    /// </summary>
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     This constant contains the entry count above which an acquire first removes expired
    ///     entries, so the dictionaries cannot grow without bound.
    /// </summary>
    private const int PruneThreshold = 1024;

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>
    ///     This field maps a user to the time of their last send. Every access holds
    ///     <see cref="_lock" />.
    /// </summary>
    private readonly Dictionary<Guid, DateTimeOffset> _lastSend = [];

    /// <summary>
    ///     This field maps a pending token to the start of its count and the sends so far. Every
    ///     access holds <see cref="_lock" />.
    /// </summary>
    private readonly Dictionary<string, (DateTimeOffset Start, int Sends)> _tokenSends = [];

    private readonly object _lock = new();

    /// <summary>
    ///     This method asks to send a code and, when allowed, records the send at once.
    /// </summary>
    /// <param name="userId">The user the code would be e-mailed to.</param>
    /// <param name="pendingToken">
    ///     The pending sign-in's token, or <see langword="null" /> for a signed-in caller, which
    ///     has no per-token cap.
    /// </param>
    /// <param name="retryAfter">The time to wait, when refused.</param>
    /// <returns><see langword="true" /> when the code may be sent.</returns>
    public bool TryAcquire(Guid userId, string pendingToken, out TimeSpan retryAfter)
    {
        lock (_lock)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            Prune(now);
            retryAfter = TimeSpan.Zero;

            // The per-token cap is checked first: waiting out the cooldown cannot help there.
            if (pendingToken is not null)
            {
                (DateTimeOffset Start, int Sends) tokenEntry = default;
                bool known =
                    _tokenSends.TryGetValue(pendingToken, out tokenEntry)
                    && now - tokenEntry.Start < TokenLifetime;
                if (known && tokenEntry.Sends >= MaxSendsPerPendingToken)
                {
                    retryAfter = tokenEntry.Start + TokenLifetime - now;
                    return false;
                }
            }

            if (_lastSend.TryGetValue(userId, out DateTimeOffset last) && now - last < Cooldown)
            {
                retryAfter = last + Cooldown - now;
                return false;
            }

            _lastSend[userId] = now;
            if (pendingToken is not null)
                _tokenSends[pendingToken] =
                    _tokenSends.TryGetValue(pendingToken, out (DateTimeOffset Start, int Sends) e)
                    && now - e.Start < TokenLifetime
                        ? (e.Start, e.Sends + 1)
                        : (now, 1);

            return true;
        }
    }

    /// <summary>
    ///     This method removes expired entries once either dictionary has grown past the
    ///     threshold. The caller holds the lock.
    /// </summary>
    /// <param name="now">The current time.</param>
    private void Prune(DateTimeOffset now)
    {
        if (_lastSend.Count >= PruneThreshold)
            foreach (
                Guid key in _lastSend
                    .Where(e => now - e.Value >= Cooldown)
                    .Select(e => e.Key)
                    .ToList()
            )
                _lastSend.Remove(key);

        if (_tokenSends.Count >= PruneThreshold)
            foreach (
                string key in _tokenSends
                    .Where(e => now - e.Value.Start >= TokenLifetime)
                    .Select(e => e.Key)
                    .ToList()
            )
                _tokenSends.Remove(key);
    }
}
