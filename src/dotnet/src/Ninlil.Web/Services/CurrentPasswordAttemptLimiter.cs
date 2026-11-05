// Define our namespace.
namespace Ninlil.Web.Services;

/// <summary>
///     This class limits how many wrong current passwords one account can submit to the
///     self-service password change and e-mail change (<c>UsersController.ChangeMyPassword</c>
///     and <c>UsersController.ChangeMyEmail</c>). Both actions share one budget per user id, so a
///     holder of a stolen access token gains nothing by rotating IP addresses or by alternating
///     endpoints. An account is blocked after <paramref name="maxFailures" /> wrong passwords within a
///     fixed <paramref name="window" /> that starts at the first failure, and a blocked account gets
///     <c>429 Too Many Requests</c> even for the correct password until the window ends. Only
///     failures count, and a correct password clears the count.
///     <para>
///         This is a rate limit, not Identity lockout: it never changes the account, so it cannot
///         stop the owner from signing in. It has the same shape as
///         <see cref="TwoFactorCodeAttemptLimiter" />, which guards the second-factor codes.
///         State lives in this process's memory and is not shared across cluster nodes, so the
///         limit is per node and a restart gives a fresh budget.
///     </para>
/// </summary>
/// <param name="maxFailures">How many wrong passwords an account may submit per window.</param>
/// <param name="window">The length of the window, counted from the first failure.</param>
/// <param name="timeProvider">
///     The clock for the window; <see langword="null" /> uses <see cref="TimeProvider.System" />.
///     Tests pass a fake clock.
/// </param>
public sealed class CurrentPasswordAttemptLimiter(
    int maxFailures = CurrentPasswordAttemptLimiter.DefaultMaxFailures,
    TimeSpan? window = null,
    TimeProvider timeProvider = null
)
{
    /// <summary>
    ///     This constant contains the default number of wrong passwords per window. It matches
    ///     the sign-in limit's 5 attempts and the second-factor code budget.
    /// </summary>
    public const int DefaultMaxFailures = 5;

    /// <summary>
    ///     This constant contains the entry count above which a new failure first removes expired
    ///     entries, so the dictionary cannot grow without bound.
    /// </summary>
    private const int PruneThreshold = 1024;

    private readonly TimeSpan _window = window ?? TimeSpan.FromMinutes(15);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>
    ///     This field maps a user to the start of their window and their failure count. The
    ///     dictionary is the lock object, and every access holds it.
    /// </summary>
    private readonly Dictionary<Guid, (DateTimeOffset WindowStart, int Failures)> _state = [];

    /// <summary>
    ///     This method reports whether the account has used up the budget. Call it before
    ///     verifying the password; a blocked attempt must not be verified or recorded.
    /// </summary>
    /// <param name="userId">The account whose password is being checked.</param>
    /// <param name="retryAfter">The time until the window ends, when blocked.</param>
    /// <returns><see langword="true" /> when the account must wait.</returns>
    public bool IsBlocked(Guid userId, out TimeSpan retryAfter)
    {
        lock (_state)
        {
            retryAfter = TimeSpan.Zero;
            if (!_state.TryGetValue(userId, out (DateTimeOffset WindowStart, int Failures) entry))
                return false;

            DateTimeOffset now = _timeProvider.GetUtcNow();
            if (now - entry.WindowStart >= _window)
            {
                _state.Remove(userId);
                return false;
            }

            if (entry.Failures < maxFailures)
                return false;

            retryAfter = entry.WindowStart + _window - now;
            return true;
        }
    }

    /// <summary>
    ///     This method counts one wrong password for the account, starting a new window if none
    ///     is open.
    /// </summary>
    /// <param name="userId">The account that submitted the wrong password.</param>
    public void RecordFailure(Guid userId)
    {
        lock (_state)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            if (_state.Count >= PruneThreshold)
                foreach (
                    Guid expired in _state
                        .Where(e => now - e.Value.WindowStart >= _window)
                        .Select(e => e.Key)
                        .ToList()
                )
                    _state.Remove(expired);

            if (
                _state.TryGetValue(userId, out (DateTimeOffset WindowStart, int Failures) entry)
                && now - entry.WindowStart < _window
            )
                _state[userId] = (entry.WindowStart, entry.Failures + 1);
            else
                _state[userId] = (now, 1);
        }
    }

    /// <summary>
    ///     This method clears the account's failure count after a correct password.
    /// </summary>
    /// <param name="userId">The account that submitted the correct password.</param>
    public void RecordSuccess(Guid userId)
    {
        lock (_state)
            _state.Remove(userId);
    }
}
