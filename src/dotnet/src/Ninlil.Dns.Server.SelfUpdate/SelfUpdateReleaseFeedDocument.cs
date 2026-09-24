// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate;

/// <summary>
///     This class represents the plain, self-describing JSON document
///     <see cref="SelfUpdateCheckService" /> expects to find at an operator-configured
///     <see cref="SelfUpdateOptions.ReleaseFeedUrl" />. Deliberately not shaped after any one real
///     hosting platform's own release API (GitHub's, GitLab's, or otherwise) -- see
///     <see cref="SelfUpdateOptions" />'s own doc comment for why this project cannot assume where
///     its own releases will eventually be published. An operator (or, eventually, this project's
///     own release tooling) serves a document shaped exactly like this from wherever they choose;
///     a small adapter in front of a real hosting platform's own API is this project's own
///     intended way to bridge the gap, not a hardcoded assumption baked into this class.
///     <para>
///         Expected JSON shape (every field required, this class's own deserialization is
///         intentionally strict about the fields it actually reads rather than tolerant of a
///         completely different shape it would otherwise silently misinterpret):
///         <code>
///         {
///           "version": "1.4.0",
///           "releaseUrl": "https://example.com/releases/1.4.0",
///           "publishedAtUtc": "2026-09-01T00:00:00Z",
///           "notes": "Fixes a real bug in X; adds Y."
///         }
///         </code>
///     </para>
/// </summary>
public sealed class SelfUpdateReleaseFeedDocument
{
    /// <summary>
    ///     This property contains the latest published version, as a plain, dotted version
    ///     string (<c>System.Version</c>-parseable, e.g. <c>"1.4.0"</c>) -- deliberately not a
    ///     full SemVer parser, since this project's own version scheme is exactly this shape (see
    ///     <see cref="SelfUpdateCheckService" />'s own doc comment on how the *current* running
    ///     version is determined) and a heavier SemVer dependency would buy nothing this project
    ///     actually needs yet.
    /// </summary>
    public string Version { get; set; }

    /// <summary>
    ///     This property contains a human-facing URL an operator can visit to read about, and
    ///     download, this release -- surfaced verbatim in <see cref="SelfUpdateStatus" /> and in
    ///     the "here's what to run" instructions <c>Controllers.SelfUpdateController</c> returns,
    ///     never followed or fetched automatically by this server itself.
    /// </summary>
    public string ReleaseUrl { get; set; }

    /// <summary>
    ///     This property contains when this release was published, as reported by the feed
    ///     itself -- purely informational, never compared against to decide whether an update is
    ///     available (only <see cref="Version" /> decides that).
    /// </summary>
    public DateTimeOffset? PublishedAtUtc { get; set; }

    /// <summary>
    ///     This property contains free-text release notes, surfaced verbatim in
    ///     <see cref="SelfUpdateStatus" /> so an operator can see what changed without leaving
    ///     this server's own admin console.
    /// </summary>
    public string Notes { get; set; }
}
