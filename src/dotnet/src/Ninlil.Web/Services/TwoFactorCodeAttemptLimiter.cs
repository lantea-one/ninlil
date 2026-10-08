using Microsoft.AspNetCore.Mvc;

// Define our namespace.
namespace Ninlil.Web.Services;

/// <summary>
///     This class limits how many wrong second-factor codes one user can submit. The checks in
///     <c>TwoFactorController</c> (enable, disable, re-setup) and <c>AuthController.TwoFactorLogin</c>
///     share one budget per user, so a guesser gains nothing by rotating IP addresses or by mixing
///     endpoints. A user is blocked after <see cref="MaxFailures" /> wrong codes within a fixed
///     <see cref="Window" /> that starts at the first failure. Only failures count, and a correct
///     code clears the count.
///     <para>
///         The limiter is keyed by user, not by IP address, because the management endpoints need a
///         signed-in caller. It does not touch Identity lockout, so a thief holding a stolen access
///         token cannot lock the owner out of signing in. The limits are constants and do not
///         depend on <c>AdminLoginRateLimit:Enable</c>. State lives in this process's memory and is
///         not shared across cluster nodes, so a restart or a second node gives a fresh budget.
///     </para>
/// </summary>
/// <param name="timeProvider">
///     The clock for the window; <see langword="null" /> uses <see cref="TimeProvider.System" />.
///     Tests pass a fake clock.
/// </param>
public sealed class TwoFactorCodeAttemptLimiter(TimeProvider timeProvider = null)
{
    /// <summary>
    ///     This constant contains how many wrong codes a user may submit per window.
    /// </summary>
    public const int MaxFailures = 5;

    /// <summary>
    ///     This field contains the length of the window: 15 minutes from the first failure.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    /// <summary>
    ///     This constant contains the entry count above which a new failure first removes expired
    ///     entries, so the dictionary cannot grow without bound.
    /// </summary>
    private const int PruneThreshold = 1024;

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>
    ///     This field maps a user to the start of their window and their failure count. The dictionary is
    ///     the lock object, and every access holds it.
    /// </summary>
    private readonly Dictionary<Guid, (DateTimeOffset WindowStart, int Failures)> _state = [];

    /// <summary>
    ///     This method reports whether the user has used up the budget. Call it before verifying a
    ///     code; a blocked attempt must not be verified or recorded.
    /// </summary>
    /// <param name="userId">The user whose codes are being checked.</param>
    /// <param name="retryAfter">The time until the window ends, when blocked.</param>
    /// <returns><see langword="true" /> when the user must wait.</returns>
    public bool IsBlocked(Guid userId, out TimeSpan retryAfter)
    {
        lock (_state)
        {
            retryAfter = TimeSpan.Zero;
            if (!_state.TryGetValue(userId, out (DateTimeOffset WindowStart, int Failures) entry))
                return false;

            DateTimeOffset now = _timeProvider.GetUtcNow();
            if (now - entry.WindowStart >= Window)
            {
                _state.Remove(userId);
                return false;
            }

            if (entry.Failures < MaxFailures)
                return false;

            retryAfter = entry.WindowStart + Window - now;
            return true;
        }
    }

    /// <summary>
    ///     This method counts one wrong code for the user, starting a new window if none is open.
    /// </summary>
    /// <param name="userId">The user who submitted the wrong code.</param>
    public void RecordFailure(Guid userId)
    {
        lock (_state)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            if (_state.Count >= PruneThreshold)
                foreach (
                    Guid expired in _state
                        .Where(e => now - e.Value.WindowStart >= Window)
                        .Select(e => e.Key)
                        .ToList()
                )
                    _state.Remove(expired);

            if (
                _state.TryGetValue(userId, out (DateTimeOffset WindowStart, int Failures) entry)
                && now - entry.WindowStart < Window
            )
                _state[userId] = (entry.WindowStart, entry.Failures + 1);
            else
                _state[userId] = (now, 1);
        }
    }

    /// <summary>
    ///     This method clears the user's failure count after a correct code.
    /// </summary>
    /// <param name="userId">The user who submitted a correct code.</param>
    public void RecordSuccess(Guid userId)
    {
        lock (_state)
            _state.Remove(userId);
    }

    /// <summary>
    ///     This method builds the 429 answer for a blocked user: a ProblemDetails body and a
    ///     <c>Retry-After</c> header in whole seconds, rounded up.
    /// </summary>
    /// <param name="controller">The controller answering the request.</param>
    /// <param name="retryAfter">The wait that <see cref="IsBlocked" /> reported.</param>
    public static ObjectResult TooManyRequests(ControllerBase controller, TimeSpan retryAfter) =>
        TooManyRequests(controller, retryAfter, "Too many wrong codes", "Too many wrong codes.");

    /// <summary>
    ///     This method builds the same 429 answer with its own title and opening sentence, so
    ///     every 429 in the sign-in flow has one shape: a ProblemDetails body saying how many
    ///     minutes to wait, and a <c>Retry-After</c> header in whole seconds, rounded up.
    /// </summary>
    /// <param name="controller">The controller answering the request.</param>
    /// <param name="retryAfter">The wait to report.</param>
    /// <param name="title">The ProblemDetails title.</param>
    /// <param name="reason">The first sentence of the detail, ending in a full stop.</param>
    public static ObjectResult TooManyRequests(
        ControllerBase controller,
        TimeSpan retryAfter,
        string title,
        string reason
    )
    {
        int seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
        int minutes = (int)Math.Ceiling(retryAfter.TotalMinutes);
        controller.Response.Headers.RetryAfter = seconds.ToString(
            System.Globalization.CultureInfo.InvariantCulture
        );

        return controller.Problem(
            title: title,
            detail: $"{reason} Try again in {minutes} minute{(minutes == 1 ? "" : "s")}.",
            statusCode: StatusCodes.Status429TooManyRequests
        );
    }
}
