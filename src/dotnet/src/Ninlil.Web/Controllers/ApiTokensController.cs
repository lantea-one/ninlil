using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Token;
using Ninlil.Web.Examples.ApiTokens;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Routing;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages the caller's API tokens, the credential a script or other machine
///     client uses instead of an interactive sign-in. Each endpoint needs only a signed-in caller
///     and acts on that caller's tokens alone. A token's raw value is returned once, when it is
///     created.
/// </summary>
/// <param name="apiTokenService">Issues and revokes API tokens.</param>
/// <param name="apiTokenRepository">Lists the caller's tokens.</param>
[ApiController]
[Route("api/api-tokens")]
[Authorize]
public class ApiTokensController(
    IApiTokenService apiTokenService,
    IApiTokenRepository apiTokenRepository
) : ControllerBase
{
    /// <summary>
    ///     This action lists the caller's API tokens, without their secret values.
    /// </summary>
    [HttpGet]
    [ReadFromReplica]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ApiTokenSummaryListExample))]
    public async Task<ActionResult<IReadOnlyList<ApiTokenSummary>>> List(
        CancellationToken stoppingToken
    )
    {
        Guid? userId = User.GetUserId();
        if (userId is null)
            return Unauthorized();

        IReadOnlyList<ApiTokenEntity> tokens = await apiTokenRepository.ListAsync(
            token => token.UserId == userId.Value,
            stoppingToken
        );

        return Ok(tokens.Select(ToSummary).ToList());
    }

    /// <summary>
    ///     This action issues a new static or one-time API token for the caller and returns its raw
    ///     value. The value is shown only in this response.
    /// </summary>
    [HttpPost]
    [SwaggerRequestExample(typeof(CreateApiTokenRequest), typeof(CreateApiTokenRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(CreateApiTokenResponseExample))]
    public async Task<ActionResult<CreateApiTokenResponse>> Create(
        CreateApiTokenRequest request,
        CancellationToken stoppingToken
    )
    {
        Guid? userId = User.GetUserId();
        if (userId is null)
            return Unauthorized();

        CreatedApiTokenResult created = request.OneTime
            ? await apiTokenService.CreateOneTimeTokenAsync(
                userId.Value,
                request.Name,
                request.ExpiresAtUtc,
                stoppingToken
            )
            : await apiTokenService.CreateStaticTokenAsync(
                userId.Value,
                request.Name,
                request.ExpiresAtUtc,
                stoppingToken
            );

        return Ok(new CreateApiTokenResponse(created.RawToken, ToSummary(created.Token)));
    }

    /// <summary>
    ///     This action revokes one of the caller's API tokens so it can no longer authenticate.
    ///     Returns 404 when the token does not exist or belongs to another user.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken stoppingToken)
    {
        Guid? userId = User.GetUserId();
        if (userId is null)
            return Unauthorized();

        ApiTokenEntity token = await apiTokenRepository.GetByIdAsync(id, stoppingToken);
        if (token is null || token.UserId != userId.Value)
            throw new NotFoundException("ApiToken", id.ToString());

        await apiTokenService.RevokeAsync(token, stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method converts a token entity to its API summary, without the secret value; only
    ///     <see cref="Create" /> returns that.
    /// </summary>
    private static ApiTokenSummary ToSummary(ApiTokenEntity token) =>
        new(
            token.Id,
            token.Name,
            token.Type,
            token.CreatedAtUtc,
            token.ExpiresAtUtc,
            token.ConsumedAtUtc,
            token.RevokedAtUtc
        );
}
