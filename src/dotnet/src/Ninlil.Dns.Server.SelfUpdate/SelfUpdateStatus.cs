// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate;

/// <summary>
///     This record is the result of the latest self-update check, which
///     <see cref="SelfUpdateCheckService" /> writes to <see cref="ISelfUpdateStatusCache" />
///     whether the check was periodic or manual, and <c>Controllers.SelfUpdateController</c>
///     reads. It is not persisted: after a restart the cache starts at <see cref="Empty" /> until
///     the first check completes, which is fine for an informational notice.
/// </summary>
/// <param name="Configured">
///     Whether <c>SelfUpdateOptions.ReleaseFeedUrl</c> was set at the last check. It tells "no
///     feed configured" apart from "checked and found nothing new", which
///     <paramref name="UpdateAvailable" /> alone cannot.
/// </param>
/// <param name="CurrentVersion">The version of the running process.</param>
/// <param name="LatestKnownVersion">
///     The version the feed reported at the last successful check, or <see langword="null" />
///     if none has succeeded since startup (or the last check found no feed configured).
/// </param>
/// <param name="UpdateAvailable">
///     Whether <paramref name="LatestKnownVersion" /> is strictly newer than
///     <paramref name="CurrentVersion" />.
/// </param>
/// <param name="ReleaseUrl">
///     A URL for the latest known release, as given by the feed.
/// </param>
/// <param name="ReleaseNotes">Free-text release notes, as given by the feed.</param>
/// <param name="LastCheckedAtUtc">
///     When a check was last attempted (successful or not), or <see langword="null" /> if none
///     has ever run.
/// </param>
/// <param name="LastCheckError">
///     The error message of the most recent check if it failed (the feed was unreachable,
///     returned malformed JSON, or reported a missing or unparseable version), or
///     <see langword="null" /> if it succeeded or none has run.
/// </param>
public sealed record SelfUpdateStatus(
    bool Configured,
    Version CurrentVersion,
    Version LatestKnownVersion,
    bool UpdateAvailable,
    string ReleaseUrl,
    string ReleaseNotes,
    DateTimeOffset? LastCheckedAtUtc,
    string LastCheckError
)
{
    /// <summary>
    ///     This static method builds the status used before any check has run: unconfigured, no
    ///     known latest version, no update available, never checked.
    /// </summary>
    /// <param name="currentVersion">The version of the running process.</param>
    public static SelfUpdateStatus Empty(Version currentVersion) =>
        new(false, currentVersion, null, false, null, null, null, null);
}
