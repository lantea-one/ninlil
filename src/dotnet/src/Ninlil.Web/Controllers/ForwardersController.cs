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
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers this server's own global upstream-forwarder set --
///     <see cref="DnsForwarderEntity" />, the server-wide default set
///     <c>Server.Forwarding</c>'s own <c>ForwarderQueryResolver</c> consults before falling
///     through to <c>Server.Recursive</c>'s own iterative resolution. This is a flat, top-level,
///     non-nested resource with no zone/parent relationship at all -- <b>not</b> the same thing as
///     a <see cref="ZoneEntity" />'s own <c>ForwarderAddresses</c> (a separate, per-zone override
///     for a <see cref="ZoneTypeEnum.Forwarder" /> zone, managed entirely through
///     <see cref="ZonesController" /> instead and left untouched by this controller). Every action
///     requires the <c>forwarders:read</c> or <c>forwarders:write</c> permission.
///     <para>
///         <b>No local cache-invalidation signal is needed on any write here</b>: confirmed by
///         reading <c>ForwarderQueryResolver.ResolveForwarderSetAsync</c> directly -- it opens a
///         fresh <see cref="IServiceScope" /> and calls
///         <see cref="IDnsForwarderRepository.ListEnabledOrderedAsync" /> fresh on every single
///         query, with no in-memory cache of the forwarder set itself at all. The only in-memory
///         state <c>Server.Forwarding</c> keeps (<c>ForwarderHealthTracker</c>'s per-definition-
///         string health map, and <c>ForwarderQueryResolver</c>'s own parsed-definition cache) is
///         keyed by the definition string's own content, not by this entity's <c>Id</c> -- editing
///         a forwarder to a materially different definition string is simply treated as a new,
///         not-yet-tracked forwarder the next time it is used, and deleting one just means the
///         next query's fresh repository read no longer returns it. This was a genuine question to
///         check, not an assumption, per this project's own "check the real code before assuming a
///         gap exists" convention -- no gap was found. Every write still publishes a
///         <see cref="DnsForwarderChangedEvent" />, though -- the cluster's own
///         <c>ForwarderClusterSyncEventSubscriber</c> is a real, independent second reason to
///         publish, forwarding the change to every cluster peer.
///     </para>
/// </summary>
/// <param name="forwarderRepository">The repository this controller reads and writes forwarders through.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
/// <param name="definitionParser">
///     The real parser this controller validates a caller's own <c>definitionString</c> against
///     before ever persisting it -- the same parser <c>ForwarderQueryResolver</c> itself uses at
///     query time, so a definition string accepted here is guaranteed to also be usable there.
/// </param>
/// <param name="logger">The logger this controller records an unexpectedly-unparseable stored definition to.</param>
/// <param name="eventPublisher">
///     The event bus this controller publishes a <see cref="DnsForwarderChangedEvent" /> through
///     on every write, so the cluster's own <c>ForwarderClusterSyncEventSubscriber</c> forwards the
///     change to every cluster peer.
/// </param>
/// <param name="clusterOptions">
///     This node's own cluster configuration, read only for its own node id -- stamped onto
///     <c>DnsForwarderEntity.OriginatingNodeId</c> on every create/update.
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
    ///     This action lists every global upstream forwarder this server knows about.
    /// </summary>
    [HttpGet]
    [RequirePermission("forwarders:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ForwarderDetailPagedResultExample))]
    public async Task<ActionResult<PagedResult<ForwarderDetail>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        // Both the page itself and its total count are pushed down to the storage engine -- a
        // real, translated ORDER BY/OFFSET/LIMIT (or the MongoDB equivalent) -- rather than
        // materializing every forwarder into memory first.
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
    ///     This action looks up a single global upstream forwarder by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
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
    ///     This action creates a new global upstream forwarder.
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
    ///     This action fully replaces an existing global upstream forwarder's fields.
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
    ///     This action partially updates an existing global upstream forwarder via RFC 7396 JSON
    ///     Merge Patch: a field absent from the patch document is left untouched, a field present
    ///     with a JSON <c>null</c> clears it (where the underlying field is itself nullable), and
    ///     any other present field overwrites it.
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
    ///     This action permanently deletes a global upstream forwarder. No zone or other entity
    ///     references a global forwarder by identifier -- unlike a TSIG key's own soft-referenced
    ///     <c>TsigKeyName</c> or a cloud credential's own <c>CloudProviderCredentialId</c>, this
    ///     entity is only ever consulted as a set, read fresh on every query -- so there is nothing
    ///     to check or clean up before removing it.
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
    ///     This method looks up a forwarder by its exact name -- <see cref="IDnsForwarderRepository" />
    ///     exposes no dedicated <c>FindByNameAsync</c> lookup of its own (unlike, say,
    ///     <c>ITsigKeyRepository</c>), so this controller filters through the base
    ///     <see cref="IRepository{TEntity}.ListAsync" /> instead -- acceptable at this project's
    ///     own homelab/small-org target scale, the same reasoning already applied to this
    ///     controller's own in-memory pagination.
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
    ///     This method validates a caller-supplied forwarder definition string against the real
    ///     <see cref="IUpstreamForwarderDefinitionParser" /> (which itself delegates to
    ///     <c>DnsCryptStampParser</c> for an <c>sdns://</c> stamp) -- rejecting a definition string
    ///     that <c>ForwarderQueryResolver</c> itself would silently skip at query time (logging a
    ///     warning and moving on to the next candidate) with an immediate, specific <c>400</c>
    ///     instead, mirroring <c>RecordsController</c>'s own "validate against the real codec, not
    ///     just accept a string" discipline for a record's own <c>data</c> field.
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
    ///     This method converts a forwarder entity into its REST representation, computing
    ///     <see cref="ForwarderDetail.Protocol" /> fresh via the real, injected
    ///     <see cref="IUpstreamForwarderDefinitionParser" /> -- a stored definition string was
    ///     already validated through the exact same parser at write time, so parsing it again here
    ///     should never actually fail; the <see langword="null" /> fallback exists only to keep a
    ///     read endpoint from ever throwing over a row some other, non-REST write path (a direct
    ///     repository call, a future import tool) may have inserted without this controller's own
    ///     validation.
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
