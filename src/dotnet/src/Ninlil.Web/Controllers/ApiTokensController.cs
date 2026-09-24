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
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages the current caller's own API tokens: the credential a
///     machine-to-machine caller authenticates with instead of signing in interactively. A
///     token's raw value is only ever returned once, at the moment it is created.
/// </summary>
/// <param name="apiTokenService">Issues and revokes API tokens.</param>
/// <param name="apiTokenRepository">Lists the current caller's own tokens.</param>
[ApiController]
[Route("api/api-tokens")]
[Authorize]
public class ApiTokensController(
    IApiTokenService apiTokenService,
    IApiTokenRepository apiTokenRepository
) : ControllerBase
{
    /// <summary>
    ///     This action lists every API token belonging to the current caller, without exposing
    ///     any token's own secret value.
    /// </summary>
    [HttpGet]
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
    ///     This action issues a new API token for the current caller, returning its raw, secret
    ///     value -- shown this one time only.
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
    ///     This action revokes one of the current caller's own API tokens, so it can no longer be
    ///     used to authenticate.
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
    ///     This method converts an API token entity into its REST representation, deliberately
    ///     omitting the token's own secret value -- only <see cref="Create" /> ever returns that,
    ///     and only once.
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
