// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate;

/// <summary>
///     This record represents the current, in-memory snapshot <see cref="SelfUpdateCheckService" />
///     maintains and <see cref="ISelfUpdateStatusCache" /> serves back to a reader -- both the
///     periodic background loop and a manual "check now" REST action write into the same cache,
///     so both paths are observed identically by <c>Controllers.SelfUpdateController</c>. Never
///     persisted -- a restart simply starts this cache back at <see cref="Empty" /> until the
///     first real check completes, which is acceptable for a purely informational notice with no
///     correctness consequence of its own.
/// </summary>
/// <param name="Configured">
///     Whether a real <c>SelfUpdateOptions.ReleaseFeedUrl</c> is actually set -- distinguishes
///     "this feature has never been pointed at anything yet" from "it was checked and found
///     nothing new," which a bare <see langword="false" /> <paramref name="UpdateAvailable" />
///     could not tell apart on its own.
/// </param>
/// <param name="CurrentVersion">This running process's own current version.</param>
/// <param name="LatestKnownVersion">
///     The newest version the release feed has ever reported, or <see langword="null" /> if no
///     check has ever completed successfully.
/// </param>
/// <param name="UpdateAvailable">
///     Whether <paramref name="LatestKnownVersion" /> is strictly newer than
///     <paramref name="CurrentVersion" /> -- the one field this feature exists to surface.
/// </param>
/// <param name="ReleaseUrl">
///     A human-facing URL for the latest known release, verbatim from the feed.
/// </param>
/// <param name="ReleaseNotes">Free-text release notes, verbatim from the feed.</param>
/// <param name="LastCheckedAtUtc">
///     When a check was last attempted (successful or not), or <see langword="null" /> if none
///     has ever run.
/// </param>
/// <param name="LastCheckError">
///     A short, human-readable description of the most recent check's own failure (the feed was
///     unreachable, returned malformed JSON, or reported an unparseable version), or
///     <see langword="null" /> if the most recent check succeeded (or none has run yet).
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
    ///     This static method builds the cache's own starting value, before any real check has
    ///     ever run -- unconfigured, no known latest version, no update available, nothing checked
    ///     yet.
    /// </summary>
    /// <param name="currentVersion">This running process's own current version.</param>
    public static SelfUpdateStatus Empty(Version currentVersion) =>
        new(false, currentVersion, null, false, null, null, null, null);
}
