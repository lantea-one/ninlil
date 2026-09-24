using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.SelfUpdate;
using Ninlil.Web.Examples.SelfUpdate;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads the self-update checker's own currently-known status, and lets an
///     operator trigger a real, immediate re-check on demand. It deliberately has **no** "apply
///     the update now" action -- see this class's own doc comment on <see cref="Get" /> for why.
///     Reading the status requires <c>selfupdate:read</c>; triggering a manual check requires
///     <c>selfupdate:manage</c>, this project's own established naming for a permission that
///     triggers a real, immediate action rather than merely reading or writing configuration (the
///     same split <c>ServicesController</c>'s own <c>services:manage</c> and
///     <c>StorageController</c>'s own <c>storage:manage</c> already establish).
/// </summary>
/// <param name="checkService">The service this controller asks to run an immediate, on-demand check.</param>
/// <param name="statusCache">The cache this controller reads the currently-known status from.</param>
[ApiController]
[Route("api/self-update")]
public class SelfUpdateController(
    SelfUpdateCheckService checkService,
    ISelfUpdateStatusCache statusCache
) : ControllerBase
{
    /// <summary>
    ///     This action reads the self-update checker's own currently-known status -- whatever the
    ///     most recent check (automatic or manually triggered) last recorded, never a fresh check
    ///     of its own. See <see cref="Check" /> to trigger a fresh one.
    ///     <para>
    ///         <b>Why there is no "apply now" button.</b> This server has no real, safe, in-process
    ///         way to replace its own running binary -- there is no installer, no systemd-unit
    ///         management, and no established release-artifact packaging for this project yet
    ///         (<see cref="SelfUpdateOptions" />'s own doc comment covers the parallel gap on the
    ///         feed side). Building a fake button that reports success without genuinely
    ///         re-executing a new binary would be actively dishonest, and building a real one
    ///         without that missing infrastructure would risk leaving a homelab DNS server
    ///         half-replaced or simply dead, with nobody around to notice. Instead,
    ///         <see cref="SelfUpdateStatusDetail.UpdateInstructions" /> tells an operator, in plain
    ///         language, exactly what to do by hand -- an honest instruction rather than an action
    ///         this project cannot yet perform safely.
    ///     </para>
    /// </summary>
    [HttpGet]
    [RequirePermission("selfupdate:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(SelfUpdateStatusDetailExample))]
    public ActionResult<SelfUpdateStatusDetail> Get() => Ok(ToDetail(statusCache.Current));

    /// <summary>
    ///     This action triggers a real, immediate check against the currently-configured release
    ///     feed, waits for it to finish, and returns the fresh result -- the same underlying check
    ///     <see cref="SelfUpdateCheckService" />'s own periodic loop runs automatically, just run
    ///     on demand rather than waited out.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    [HttpPost("check")]
    [RequirePermission("selfupdate:manage")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(SelfUpdateStatusDetailExample))]
    public async Task<ActionResult<SelfUpdateStatusDetail>> Check(CancellationToken stoppingToken)
    {
        SelfUpdateStatus status = await checkService.CheckNowAsync(stoppingToken);
        return Ok(ToDetail(status));
    }

    /// <summary>
    ///     This method builds the REST-facing <see cref="SelfUpdateStatusDetail" /> from an
    ///     internal <see cref="SelfUpdateStatus" /> snapshot, including the plain-language
    ///     <see cref="SelfUpdateStatusDetail.UpdateInstructions" /> text -- see <see cref="Get" />'s
    ///     own doc comment for why this project surfaces an instruction rather than a button.
    /// </summary>
    private static SelfUpdateStatusDetail ToDetail(SelfUpdateStatus status) =>
        new(
            status.Configured,
            status.CurrentVersion?.ToString(),
            status.LatestKnownVersion?.ToString(),
            status.UpdateAvailable,
            status.ReleaseUrl,
            status.ReleaseNotes,
            status.LastCheckedAtUtc,
            status.LastCheckError,
            BuildInstructions(status)
        );

    /// <summary>
    ///     This method builds the plain-language instructions an operator sees for the current
    ///     status -- what, if anything, they should actually do about it.
    /// </summary>
    private static string BuildInstructions(SelfUpdateStatus status)
    {
        if (!status.Configured)
            return "No release feed is configured yet -- set one in Self-Update Settings to enable checking for a newer release.";

        if (!status.UpdateAvailable)
            return "This server is already running the latest known version. No action needed.";

        string releaseUrlPart = string.IsNullOrWhiteSpace(status.ReleaseUrl)
            ? string.Empty
            : $" from {status.ReleaseUrl}";

        return $"Version {status.LatestKnownVersion} is available (currently running {status.CurrentVersion}). "
            + $"Download the new release{releaseUrlPart}, replace this server's own binary, and restart it -- "
            + "this project has no automatic in-place updater, so applying an update is always a manual step.";
    }
}
