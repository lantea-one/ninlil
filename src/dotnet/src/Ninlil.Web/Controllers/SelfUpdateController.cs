using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.SelfUpdate;
using Ninlil.Web.Examples.SelfUpdate;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reports what the self-update checker knows about newer releases and lets
///     an operator run a check on demand. It has no "apply the update" action (see
///     <see cref="Get" />). Reading needs <c>selfupdate:read</c>; running a check needs
///     <c>selfupdate:manage</c>, the naming used for permissions that trigger an action rather
///     than read or write settings (as with <c>services:manage</c> and <c>storage:manage</c>).
/// </summary>
/// <param name="checkService">Runs an on-demand check.</param>
/// <param name="statusCache">Holds the result of the latest check.</param>
[ApiController]
[Route("api/self-update")]
public class SelfUpdateController(
    SelfUpdateCheckService checkService,
    ISelfUpdateStatusCache statusCache
) : ControllerBase
{
    /// <summary>
    ///     This action returns the result of the latest check, automatic or manual, without
    ///     checking again; <see cref="Check" /> runs a new one. Requires <c>selfupdate:read</c>.
    ///     <para>
    ///         <b>Why there is no "apply now" action.</b> The server does not replace its own
    ///         running binary: it is installed from a package, container or install script, and a
    ///         failed in-place update could leave a home DNS server broken with nobody watching.
    ///         Instead, <see cref="SelfUpdateStatusDetail.UpdateInstructions" /> tells the operator
    ///         what to do by hand.
    ///     </para>
    /// </summary>
    [HttpGet]
    [RequirePermission("selfupdate:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(SelfUpdateStatusDetailExample))]
    public ActionResult<SelfUpdateStatusDetail> Get() => Ok(ToDetail(statusCache.Current));

    /// <summary>
    ///     This action checks the configured release feed now, waits for the result and returns
    ///     it. It runs the same check as the periodic loop in
    ///     <see cref="SelfUpdateCheckService" />. Requires <c>selfupdate:manage</c>.
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
    ///     This method builds the API's <see cref="SelfUpdateStatusDetail" /> from a
    ///     <see cref="SelfUpdateStatus" />, including the plain-language
    ///     <see cref="SelfUpdateStatusDetail.UpdateInstructions" />.
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
    ///     This method builds the plain-language instructions for the current status: what, if
    ///     anything, the operator should do.
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
