// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>SelfUpdateOptions</c>. No secret material is involved, so this record's own fields are
///     the exact same ones a <c>PATCH</c> to the same endpoint accepts.
/// </summary>
/// <param name="EnableAutomaticCheck">Whether this host periodically checks <see cref="ReleaseFeedUrl" /> at all.</param>
/// <param name="ReleaseFeedUrl">The release feed URL checked, or an empty string if none is configured yet.</param>
/// <param name="CheckInterval">How often a real check is due.</param>
public record SelfUpdateSettingsDetail(
    bool EnableAutomaticCheck,
    string ReleaseFeedUrl,
    TimeSpan CheckInterval
);

/// <summary>
///     This record is the REST representation of the self-update checker's own currently-known
///     status -- the one field this whole feature exists to surface, plus enough context for an
///     operator to act on it. See <c>Controllers.SelfUpdateController</c>'s own doc comment for
///     what "acting on it" means today.
/// </summary>
/// <param name="Configured">Whether a real release feed URL has ever been set.</param>
/// <param name="CurrentVersion">This running process's own current version, as a plain dotted string.</param>
/// <param name="LatestKnownVersion">
///     The newest version the release feed has ever reported, as a plain dotted string, or
///     <see langword="null" /> if no check has ever completed successfully.
/// </param>
/// <param name="UpdateAvailable">Whether a genuinely newer release is currently known.</param>
/// <param name="ReleaseUrl">A human-facing URL for the latest known release.</param>
/// <param name="ReleaseNotes">Free-text release notes for the latest known release.</param>
/// <param name="LastCheckedAtUtc">When a check was last attempted, or <see langword="null" /> if none has ever run.</param>
/// <param name="LastCheckError">A short description of the most recent check's own failure, or <see langword="null" /> if it succeeded.</param>
/// <param name="UpdateInstructions">
///     A plain-language description of what an operator should actually do about
///     <see cref="UpdateAvailable" /> -- see <c>Controllers.SelfUpdateController</c>'s own doc
///     comment for why this is honest, written guidance rather than a button that silently does
///     nothing.
/// </param>
public record SelfUpdateStatusDetail(
    bool Configured,
    string CurrentVersion,
    string LatestKnownVersion,
    bool UpdateAvailable,
    string ReleaseUrl,
    string ReleaseNotes,
    DateTimeOffset? LastCheckedAtUtc,
    string LastCheckError,
    string UpdateInstructions
);
