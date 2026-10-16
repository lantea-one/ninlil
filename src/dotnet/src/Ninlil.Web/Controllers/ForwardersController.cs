using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Web.Examples.Forwarders;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Routing;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages the server-wide upstream forwarders
///     (<see cref="DnsForwarderEntity" />) that <c>ForwarderQueryResolver</c> in
///     <c>Server.Forwarding</c> tries before falling back to iterative resolution in
///     <c>Server.Recursive</c>. These are not the per-zone <c>ForwarderAddresses</c> of a
///     <see cref="ZoneTypeEnum.Forwarder" /> zone, which <see cref="ZonesController" /> manages.
///     Reads need <c>forwarders:read</c> and changes need <c>forwarders:write</c>.
///     <para>
///         <b>A change applies to the next query without any cache reset.</b>
///         <c>ForwarderQueryResolver.ResolveForwarderSetAsync</c> reads the enabled forwarders
///         through <see cref="IDnsForwarderRepository.ListEnabledOrderedAsync" /> in a new
///         <see cref="IServiceScope" /> on every query. <c>ForwarderEndpointCache</c> (parsed
///         definitions and the resolved addresses of host names) and
///         <c>ForwarderHealthTracker</c>'s health map are keyed by the definition string, not by
///         <c>Id</c>, so an edited definition is simply treated as a new forwarder. Every write
///         still publishes a <see cref="DnsForwarderChangedEvent" />, which
///         <c>ForwarderClusterSyncEventSubscriber</c> sends to the other cluster nodes.
///     </para>
/// </summary>
/// <param name="forwarderRepository">Reads and writes forwarders.</param>
/// <param name="unitOfWork">Commits staged changes.</param>
/// <param name="definitionParser">
///     Validates the syntax of a <c>definitionString</c> before it is saved. The parser never
///     queries DNS, so a host name that does not resolve is saved; <c>ForwarderEndpointCache</c>
///     resolves it in the background.
/// </param>
/// <param name="logger">Records a stored definition that unexpectedly fails to parse.</param>
/// <param name="eventPublisher">
///     Publishes <see cref="DnsForwarderChangedEvent" /> on every write so
///     <c>ForwarderClusterSyncEventSubscriber</c> sends the change to every cluster peer.
/// </param>
/// <param name="clusterOptions">
///     Supplies this node's cluster node id, stamped onto
///     <c>DnsForwarderEntity.OriginatingNodeId</c> on every create and update.
/// </param>
[ApiController]
[Route("api/forwarders")]
public class ForwardersController(
    IDnsForwarderRepository forwarderRepository,
    IUnitOfWork unitOfWork,
    IUpstreamForwarderDefinitionParser definitionParser,
    ILogger<ForwardersController> logger,
    IDnsServerEventPublisher eventPublisher,
    IOptionsMonitor<ClusterOptions> clusterOptions
) : ControllerBase
{
    /// <summary>
    ///     This action lists the server-wide forwarders, one page at a time, each with the protocol
    ///     parsed from its definition. Requires <c>forwarders:read</c>.
    /// </summary>
    [HttpGet]
    [ReadFromReplica]
    [RequirePermission("forwarders:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ForwarderDetailPagedResultExample))]
    public async Task<ActionResult<PagedResult<ForwarderDetail>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        // The page and the total count are both computed by the storage engine (ORDER
        // BY/OFFSET/LIMIT or the MongoDB equivalent), not by loading every forwarder into memory.
        IReadOnlyList<DnsForwarderEntity> forwarders = await forwarderRepository.SearchAsync(
            skip,
            take,
            stoppingToken
        );
        int totalCount = await forwarderRepository.CountAsync(stoppingToken);

        return Ok(
            new PagedResult<ForwarderDetail>(
                [.. forwarders.Select(ToDetail)],
                skip,
                take,
                totalCount
            )
        );
    }

    /// <summary>
    ///     This action returns one forwarder. Returns 404 for an unknown id. Requires
    ///     <c>forwarders:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ReadFromReplica]
    [RequirePermission("forwarders:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ForwarderDetailExample))]
    public async Task<ActionResult<ForwarderDetail>> Get(Guid id, CancellationToken stoppingToken)
    {
        DnsForwarderEntity forwarder = await forwarderRepository.GetByIdAsync(id, stoppingToken);
        if (forwarder is null)
            throw new NotFoundException("Forwarder", id.ToString());

        return Ok(ToDetail(forwarder));
    }

    /// <summary>
    ///     This action creates a server-wide forwarder. Returns 400 for a missing name or a
    ///     definition string whose syntax the forwarder parser rejects, and 409 when the name is
    ///     taken. A host name is not looked up when the forwarder is saved, so one that does not
    ///     resolve is accepted. Requires <c>forwarders:write</c>.
    /// </summary>
    [HttpPost]
    [RequirePermission("forwarders:write")]
    [SwaggerRequestExample(typeof(ForwarderWriteRequest), typeof(ForwarderWriteRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(ForwarderDetailExample))]
    public async Task<ActionResult<ForwarderDetail>> Create(
        ForwarderWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        if (string.IsNullOrWhiteSpace(request?.Name))
            throw new ValidationException("'name' is required.");

        if (await FindByNameAsync(request.Name, stoppingToken) is not null)
            throw new ConflictException($"A forwarder named '{request.Name}' already exists.");

        if (!TryValidateDefinitionString(request.DefinitionString, out string definitionError))
            throw new ValidationException(definitionError);

        DnsForwarderEntity forwarder = new()
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            DefinitionString = request.DefinitionString,
            SortOrder = request.SortOrder,
            IsDisabled = request.IsDisabled,
            Comments = request.Comments,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            OriginatingNodeId = clusterOptions.CurrentValue.NodeId,
        };

        await forwarderRepository.AddAsync(forwarder, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(new DnsForwarderChangedEvent(DateTimeOffset.UtcNow, forwarder.Id));

        return CreatedAtAction(nameof(Get), new { id = forwarder.Id }, ToDetail(forwarder));
    }

    /// <summary>
    ///     This action replaces a forwarder's fields. Validation and errors are as for
    ///     <see cref="Create" />, plus 404 for an unknown id. Requires <c>forwarders:write</c>.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("forwarders:write")]
    [SwaggerRequestExample(typeof(ForwarderWriteRequest), typeof(ForwarderWriteRequestExample))]
    public async Task<IActionResult> Replace(
        Guid id,
        ForwarderWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        DnsForwarderEntity forwarder = await forwarderRepository.GetByIdAsync(id, stoppingToken);
        if (forwarder is null)
            throw new NotFoundException("Forwarder", id.ToString());

        if (string.IsNullOrWhiteSpace(request?.Name))
            throw new ValidationException("'name' is required.");

        DnsForwarderEntity existingByName = await FindByNameAsync(request.Name, stoppingToken);
        if (existingByName is not null && existingByName.Id != id)
            throw new ConflictException($"A forwarder named '{request.Name}' already exists.");

        if (!TryValidateDefinitionString(request.DefinitionString, out string definitionError))
            throw new ValidationException(definitionError);

        forwarder.Name = request.Name;
        forwarder.DefinitionString = request.DefinitionString;
        forwarder.SortOrder = request.SortOrder;
        forwarder.IsDisabled = request.IsDisabled;
        forwarder.Comments = request.Comments;
        forwarder.UpdatedAtUtc = DateTimeOffset.UtcNow;
        forwarder.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;

        forwarderRepository.Update(forwarder);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(new DnsForwarderChangedEvent(DateTimeOffset.UtcNow, forwarder.Id));

        return NoContent();
    }

    /// <summary>
    ///     This action updates a forwarder as an RFC 7396 merge patch: an absent field is
    ///     unchanged, a JSON <c>null</c> clears the comments, and any other value replaces it.
    ///     Validation and errors are as for <see cref="Replace" />. Requires
    ///     <c>forwarders:write</c>.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [RequirePermission("forwarders:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(ForwarderPatchRequestExample))]
    public async Task<IActionResult> Patch(
        Guid id,
        [FromBody] JsonElement patch,
        CancellationToken stoppingToken
    )
    {
        DnsForwarderEntity forwarder = await forwarderRepository.GetByIdAsync(id, stoppingToken);
        if (forwarder is null)
            throw new NotFoundException("Forwarder", id.ToString());

        if (patch.TryGetMergePatchField("name", out JsonElement name))
        {
            string newName = name.GetString();
            if (string.IsNullOrWhiteSpace(newName))
                throw new ValidationException("'name' must not be empty.");

            DnsForwarderEntity existingByName = await FindByNameAsync(newName, stoppingToken);
            if (existingByName is not null && existingByName.Id != id)
                throw new ConflictException($"A forwarder named '{newName}' already exists.");

            forwarder.Name = newName;
        }

        if (
            patch.TryGetMergePatchField("definitionString", out JsonElement definitionStringElement)
        )
        {
            string newDefinitionString = definitionStringElement.GetString();
            if (!TryValidateDefinitionString(newDefinitionString, out string definitionError))
                throw new ValidationException(definitionError);

            forwarder.DefinitionString = newDefinitionString;
        }

        if (patch.TryGetMergePatchField("sortOrder", out JsonElement sortOrder))
            forwarder.SortOrder = sortOrder.GetInt32();

        if (patch.TryGetMergePatchField("isDisabled", out JsonElement isDisabled))
            forwarder.IsDisabled = isDisabled.GetBoolean();

        if (patch.TryGetMergePatchField("comments", out JsonElement comments))
            forwarder.Comments =
                comments.ValueKind == JsonValueKind.Null ? null : comments.GetString();

        forwarder.UpdatedAtUtc = DateTimeOffset.UtcNow;
        forwarder.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;
        forwarderRepository.Update(forwarder);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(new DnsForwarderChangedEvent(DateTimeOffset.UtcNow, forwarder.Id));

        return NoContent();
    }

    /// <summary>
    ///     This action deletes a forwarder. Nothing refers to a forwarder by id (the set is read
    ///     whole on every query), so nothing needs checking or cleaning up first. Returns 404 for
    ///     an unknown id. Requires <c>forwarders:write</c>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("forwarders:write")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken stoppingToken)
    {
        DnsForwarderEntity forwarder = await forwarderRepository.GetByIdAsync(id, stoppingToken);
        if (forwarder is null)
            throw new NotFoundException("Forwarder", id.ToString());

        forwarderRepository.Remove(forwarder);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(
            new DnsForwarderChangedEvent(DateTimeOffset.UtcNow, id, IsDeleted: true)
        );

        return NoContent();
    }

    /// <summary>
    ///     This method finds a forwarder by exact name. <see cref="IDnsForwarderRepository" /> has
    ///     no <c>FindByNameAsync</c> (unlike <c>ITsigKeyRepository</c>), so it filters through
    ///     <see cref="IRepository{TEntity}.ListAsync" />.
    /// </summary>
    private async Task<DnsForwarderEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken
    )
    {
        IReadOnlyList<DnsForwarderEntity> matches = await forwarderRepository.ListAsync(
            forwarder => forwarder.Name == name,
            stoppingToken
        );
        return matches.FirstOrDefault();
    }

    /// <summary>
    ///     This method checks a definition string with
    ///     <see cref="IUpstreamForwarderDefinitionParser" /> (which hands <c>sdns://</c> stamps to
    ///     <c>DnsCryptStampParser</c>). The check is syntactic and never queries DNS. A string that
    ///     <c>ForwarderQueryResolver</c> would skip with a warning at query time is rejected here
    ///     with a 400 instead.
    /// </summary>
    private bool TryValidateDefinitionString(string definitionString, out string error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(definitionString))
        {
            error = "'definitionString' is required.";
            return false;
        }

        try
        {
            definitionParser.Parse(definitionString);
            return true;
        }
        catch (FormatException exception)
        {
            error =
                $"'{definitionString}' is not a valid forwarder definition: {exception.Message}";
            return false;
        }
    }

    /// <summary>
    ///     This method converts a forwarder entity to its API representation, parsing the
    ///     definition string to fill in <see cref="ForwarderDetail.Protocol" />. A string saved
    ///     through this controller always parses; for a row written another way (cluster sync, for
    ///     example) the protocol is left <see langword="null" /> and a warning logged, so a read
    ///     never fails.
    /// </summary>
    private ForwarderDetail ToDetail(DnsForwarderEntity forwarder)
    {
        UpstreamForwarderProtocolEnum? protocol = null;

        try
        {
            protocol = definitionParser.Parse(forwarder.DefinitionString).Protocol;
        }
        catch (FormatException exception)
        {
            logger.LogWarning(
                exception,
                "Forwarder '{Name}' ({Id}) carries an unparseable definition string.",
                forwarder.Name,
                forwarder.Id
            );
        }

        return new ForwarderDetail(
            forwarder.Id,
            forwarder.Name,
            forwarder.DefinitionString,
            protocol,
            forwarder.SortOrder,
            forwarder.IsDisabled,
            forwarder.Comments,
            forwarder.CreatedAtUtc,
            forwarder.UpdatedAtUtc,
            forwarder.OriginatingNodeId
        );
    }
}
