// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the update check settings (<c>SelfUpdateOptions</c>) that
///     <c>GET /api/self-update-settings</c> returns. A <c>PATCH</c> to the same endpoint accepts
///     the same fields.
/// </summary>
/// <param name="EnableAutomaticCheck">Whether the server checks <see cref="ReleaseFeedUrl" /> periodically. Defaults to <see langword="true" />.</param>
/// <param name="ReleaseFeedUrl">The release feed URL to check, or an empty string (the default) if none is set.</param>
/// <param name="CheckInterval">How often a check is due. Defaults to 24 hours.</param>
public record SelfUpdateSettingsDetail(
    bool EnableAutomaticCheck,
    string ReleaseFeedUrl,
    TimeSpan CheckInterval
);

/// <summary>
///     This record is what the update checker knows, as returned by <c>GET /api/self-update</c>
///     and <c>POST /api/self-update/check</c>: whether an update is available, with enough context
///     to act on it. <c>Controllers.SelfUpdateController</c> explains how an update is applied.
/// </summary>
/// <param name="Configured">Whether a release feed URL is set.</param>
/// <param name="CurrentVersion">The running version, as a plain dotted string.</param>
/// <param name="LatestKnownVersion">
///     The newest version the release feed has reported, as a plain dotted string, or
///     <see langword="null" /> if no check has succeeded.
/// </param>
/// <param name="UpdateAvailable">Whether a release newer than <paramref name="CurrentVersion" /> is known.</param>
/// <param name="ReleaseUrl">A human-facing URL for the latest known release.</param>
/// <param name="ReleaseNotes">Free-text release notes for the latest known release.</param>
/// <param name="LastCheckedAtUtc">When a check was last attempted, or <see langword="null" /> if none has run.</param>
/// <param name="LastCheckError">A short description of why the last check failed, or <see langword="null" /> if it succeeded.</param>
/// <param name="UpdateInstructions">
///     Plain-language guidance on how to update. The server does not update itself, so the UI
///     shows these instructions rather than an update button (see
///     <c>Controllers.SelfUpdateController</c>).
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
