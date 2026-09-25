// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate;

/// <summary>
///     This class represents the JSON document <see cref="SelfUpdateCheckService" /> expects at
///     <see cref="SelfUpdateOptions.ReleaseFeedUrl" />. It follows no hosting platform's release
///     API (GitHub, GitLab or otherwise), so it can be served from anywhere, with an adapter in
///     front of a platform API if needed (see <see cref="SelfUpdateOptions" />).
///     <para>
///         Expected JSON shape. Only <c>version</c> is required; a missing or unparseable
///         version fails the check. The other fields are shown as given and may be absent.
///         <code>
///         {
///           "version": "2026.9.24.1",
///           "releaseUrl": "https://example.com/releases/2026.9.24.1",
///           "publishedAtUtc": "2026-09-24T00:00:00Z",
///           "notes": "Fixes a bug in X; adds Y."
///         }
///         </code>
///     </para>
/// </summary>
public sealed class SelfUpdateReleaseFeedDocument
{
    /// <summary>
    ///     This property contains the latest published version as a dotted string that
    ///     <see cref="System.Version" /> can parse. It is compared with the running assembly
    ///     version (see <see cref="DefaultCurrentAssemblyVersionProvider" />), which the build
    ///     pipeline stamps in the numeric <c>yyyy.m.d.increment</c> form, so the feed must use
    ///     that form (for example <c>"2026.9.24.1"</c>), not the <c>v…R…</c> release name. No
    ///     SemVer parser is needed.
    /// </summary>
    public string Version { get; set; }

    /// <summary>
    ///     This property contains a URL where an operator can read about and download the release.
    ///     It is shown as given in <see cref="SelfUpdateStatus" /> and in the instructions
    ///     <c>Controllers.SelfUpdateController</c> returns; the server never fetches it.
    /// </summary>
    public string ReleaseUrl { get; set; }

    /// <summary>
    ///     This property contains when the release was published, as reported by the feed. Only
    ///     <see cref="Version" /> decides whether an update is available; this value is not
    ///     copied into <see cref="SelfUpdateStatus" />.
    /// </summary>
    public DateTimeOffset? PublishedAtUtc { get; set; }

    /// <summary>
    ///     This property contains free-text release notes, shown as given in
    ///     <see cref="SelfUpdateStatus" /> so an operator can see what changed from the web UI.
    /// </summary>
    public string Notes { get; set; }
}
