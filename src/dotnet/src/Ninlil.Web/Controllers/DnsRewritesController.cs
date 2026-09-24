using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Rewrites;
using Ninlil.Web.Examples.Rewrites;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers this project's DNS rewrites --
///     <see cref="DnsRewriteEntity" />, the lightweight <c>domain -&gt; target</c> override
///     <c>Server.Rewrites</c>'s own <c>DnsRewriteEngine</c> consults before zone/forwarder/
///     recursive resolution. This is a flat, top-level, non-nested resource with no zone/parent
///     relationship at all, matching <see cref="ForwardersController" />'s/
///     <see cref="WebhooksController" />'s own precedent. Every action requires the
///     <c>dnsrewrites:read</c> or <c>dnsrewrites:write</c> permission.
///     <para>
///         <b>No local cache-invalidation signal is needed on any write here</b>:
///         <c>DnsRewriteEngine.TryBuildResponseAsync</c> opens a fresh <see cref="IServiceScope" />
///         and calls <see cref="IDnsRewriteRepository.ListEnabledAsync" /> fresh on every single
///         query, with no in-memory cache of the rewrite set itself at all -- the same
///         "check the real code before assuming a gap exists" convention already applied to
///         <see cref="ForwardersController" />'s own identical question. Every
///         write still publishes a <see cref="DnsRewriteChangedEvent" />, though -- the cluster's
///         own <c>RewriteClusterSyncEventSubscriber</c> is a real, independent second reason to
///         publish, forwarding the change to every cluster peer.
///     </para>
///     <para>
///         <b>Duplicate detection</b>: a rewrite's own <see cref="DnsRewriteEntity.Domain" /> is
///         deliberately <b>not</b> unique at the storage layer (an operator may legitimately
///         configure separate A-target and AAAA-target rewrites for the same domain), so this
///         controller instead rejects, with <c>409 Conflict</c>, a create/replace whose
///         <see cref="DnsRewriteEntity.Domain" /> (normalized) <em>and</em> classified
///         <see cref="DnsRewriteTargetKindEnum" /> both already match an existing rewrite --
///         the ambiguous case (two A-target rewrites for the same domain) this project's own
///         REST layer should never silently accept.
///     </para>
/// </summary>
/// <param name="rewriteRepository">The repository this controller reads and writes rewrites through.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
/// <param name="eventPublisher">
///     The event bus this controller publishes a <see cref="DnsRewriteChangedEvent" /> through on
///     every write, so the cluster's own <c>RewriteClusterSyncEventSubscriber</c> forwards the
///     change to every cluster peer.
/// </param>
/// <param name="clusterOptions">
///     This node's own cluster configuration, read only for its own node id -- stamped onto
///     <c>DnsRewriteEntity.OriginatingNodeId</c> on every create/update.
/// </param>
[ApiController]
[Route("api/dns-rewrites")]
public class DnsRewritesController(
    IDnsRewriteRepository rewriteRepository,
    IUnitOfWork unitOfWork,
    IDnsServerEventPublisher eventPublisher,
    IOptionsMonitor<ClusterOptions> clusterOptions
) : ControllerBase
{
    /// <summary>
    ///     This action lists every DNS rewrite this server knows about.
    /// </summary>
    [HttpGet]
    [RequirePermission("dnsrewrites:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DnsRewriteDetailPagedResultExample))]
    public async Task<ActionResult<PagedResult<DnsRewriteDetail>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        // Both the page itself and its total count are pushed down to the storage engine -- a
        // real, translated ORDER BY/OFFSET/LIMIT (or the MongoDB equivalent) -- rather than
        // materializing every rewrite into memory first.
        IReadOnlyList<DnsRewriteEntity> rewrites = await rewriteRepository.SearchAsync(
            skip,
            take,
            stoppingToken
        );
        int totalCount = await rewriteRepository.CountAsync(stoppingToken);

        return Ok(
            new PagedResult<DnsRewriteDetail>(
                [.. rewrites.Select(ToDetail)],
                skip,
                take,
                totalCount
            )
        );
    }

    /// <summary>
    ///     This action looks up a single DNS rewrite by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("dnsrewrites:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DnsRewriteDetailExample))]
    public async Task<ActionResult<DnsRewriteDetail>> Get(Guid id, CancellationToken stoppingToken)
    {
        DnsRewriteEntity rewrite = await rewriteRepository.GetByIdAsync(id, stoppingToken);
        if (rewrite is null)
            throw new NotFoundException("DnsRewrite", id.ToString());

        return Ok(ToDetail(rewrite));
    }

    /// <summary>
    ///     This action creates a new DNS rewrite.
    /// </summary>
    [HttpPost]
    [RequirePermission("dnsrewrites:write")]
    [SwaggerRequestExample(typeof(DnsRewriteWriteRequest), typeof(DnsRewriteWriteRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(DnsRewriteDetailExample))]
    public async Task<ActionResult<DnsRewriteDetail>> Create(
        DnsRewriteWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        if (!TryValidateDomain(request?.Domain, out string domainError))
            throw new ValidationException(domainError);

        if (
            !DnsRewriteTargetClassifier.TryClassify(
                request.Target,
                out DnsRewriteTargetKindEnum kind,
                out string targetError
            )
        )
            throw new ValidationException(targetError);

        if (await FindConflictingAsync(request.Domain, kind, null, stoppingToken) is not null)
            throw new ConflictException(
                $"A rewrite for '{request.Domain}' answering the same record kind already exists."
            );

        DnsRewriteEntity rewrite = new()
        {
            Id = Guid.NewGuid(),
            Domain = request.Domain,
            Target = request.Target,
            IsDisabled = request.IsDisabled,
            Comments = request.Comments,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            OriginatingNodeId = clusterOptions.CurrentValue.NodeId,
        };

        await rewriteRepository.AddAsync(rewrite, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(new DnsRewriteChangedEvent(DateTimeOffset.UtcNow, rewrite.Id));

        return CreatedAtAction(nameof(Get), new { id = rewrite.Id }, ToDetail(rewrite));
    }

    /// <summary>
    ///     This action fully replaces an existing DNS rewrite's fields.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("dnsrewrites:write")]
    [SwaggerRequestExample(typeof(DnsRewriteWriteRequest), typeof(DnsRewriteWriteRequestExample))]
    public async Task<IActionResult> Replace(
        Guid id,
        DnsRewriteWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        DnsRewriteEntity rewrite = await rewriteRepository.GetByIdAsync(id, stoppingToken);
        if (rewrite is null)
            throw new NotFoundException("DnsRewrite", id.ToString());

        if (!TryValidateDomain(request?.Domain, out string domainError))
            throw new ValidationException(domainError);

        if (
            !DnsRewriteTargetClassifier.TryClassify(
                request.Target,
                out DnsRewriteTargetKindEnum kind,
                out string targetError
            )
        )
            throw new ValidationException(targetError);

        DnsRewriteEntity conflicting = await FindConflictingAsync(
            request.Domain,
            kind,
            id,
            stoppingToken
        );
        if (conflicting is not null)
            throw new ConflictException(
                $"A rewrite for '{request.Domain}' answering the same record kind already exists."
            );

        rewrite.Domain = request.Domain;
        rewrite.Target = request.Target;
        rewrite.IsDisabled = request.IsDisabled;
        rewrite.Comments = request.Comments;
        rewrite.UpdatedAtUtc = DateTimeOffset.UtcNow;
        rewrite.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;

        rewriteRepository.Update(rewrite);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(new DnsRewriteChangedEvent(DateTimeOffset.UtcNow, rewrite.Id));

        return NoContent();
    }

    /// <summary>
    ///     This action partially updates an existing DNS rewrite via RFC 7396 JSON Merge Patch: a
    ///     field absent from the patch document is left untouched, and any present field
    ///     overwrites it.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [RequirePermission("dnsrewrites:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(DnsRewritePatchRequestExample))]
    public async Task<IActionResult> Patch(
        Guid id,
        [FromBody] JsonElement patch,
        CancellationToken stoppingToken
    )
    {
        DnsRewriteEntity rewrite = await rewriteRepository.GetByIdAsync(id, stoppingToken);
        if (rewrite is null)
            throw new NotFoundException("DnsRewrite", id.ToString());

        string newDomain = rewrite.Domain;
        string newTarget = rewrite.Target;

        if (patch.TryGetMergePatchField("domain", out JsonElement domain))
        {
            newDomain = domain.GetString();
            if (!TryValidateDomain(newDomain, out string domainError))
                throw new ValidationException(domainError);
        }

        DnsRewriteTargetKindEnum kind = default;

        if (patch.TryGetMergePatchField("target", out JsonElement target))
        {
            newTarget = target.GetString();
            if (
                !DnsRewriteTargetClassifier.TryClassify(newTarget, out kind, out string targetError)
            )
                throw new ValidationException(targetError);
        }
        else
        {
            DnsRewriteTargetClassifier.TryClassify(newTarget, out kind, out _);
        }

        if (
            patch.TryGetMergePatchField("domain", out _)
            || patch.TryGetMergePatchField("target", out _)
        )
        {
            DnsRewriteEntity conflicting = await FindConflictingAsync(
                newDomain,
                kind,
                id,
                stoppingToken
            );
            if (conflicting is not null)
                throw new ConflictException(
                    $"A rewrite for '{newDomain}' answering the same record kind already exists."
                );
        }

        rewrite.Domain = newDomain;
        rewrite.Target = newTarget;

        if (patch.TryGetMergePatchField("isDisabled", out JsonElement isDisabled))
            rewrite.IsDisabled = isDisabled.GetBoolean();

        if (patch.TryGetMergePatchField("comments", out JsonElement comments))
            rewrite.Comments =
                comments.ValueKind == JsonValueKind.Null ? null : comments.GetString();

        rewrite.UpdatedAtUtc = DateTimeOffset.UtcNow;
        rewrite.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;
        rewriteRepository.Update(rewrite);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(new DnsRewriteChangedEvent(DateTimeOffset.UtcNow, rewrite.Id));

        return NoContent();
    }

    /// <summary>
    ///     This action permanently deletes a DNS rewrite. No other entity references a rewrite
    ///     by identifier, so there is nothing to check or clean up before removing it.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("dnsrewrites:write")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken stoppingToken)
    {
        DnsRewriteEntity rewrite = await rewriteRepository.GetByIdAsync(id, stoppingToken);
        if (rewrite is null)
            throw new NotFoundException("DnsRewrite", id.ToString());

        rewriteRepository.Remove(rewrite);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(
            new DnsRewriteChangedEvent(DateTimeOffset.UtcNow, id, IsDeleted: true)
        );

        return NoContent();
    }

    /// <summary>
    ///     This method validates a domain pattern -- required, and a syntactically valid DNS
    ///     name (a bare <c>*</c> label is ordinary label content as far as
    ///     <see cref="DnsName.TryParse(string, out DnsName, out string)" /> is concerned, so a
    ///     wildcard domain such as <c>*.example.com</c> already passes this same structural
    ///     check without any special-casing).
    /// </summary>
    private static bool TryValidateDomain(string domain, out string error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(domain))
        {
            error = "'domain' is required.";
            return false;
        }

        if (!DnsName.TryParse(domain, out _, out string nameError))
        {
            error = $"'{domain}' is not a valid domain name: {nameError}";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method looks up an existing rewrite whose own normalized domain and classified
    ///     target kind both already match the given pair -- the ambiguous "two rewrites of the
    ///     same record kind for the same domain" case this controller rejects. Filters through
    ///     the base <see cref="IRepository{TEntity}.ListAsync" /> rather than a dedicated
    ///     repository lookup, the same "acceptable at this project's own homelab/small-org target
    ///     scale" reasoning <see cref="ForwardersController.FindByNameAsync" /> already applies.
    /// </summary>
    private async Task<DnsRewriteEntity> FindConflictingAsync(
        string domain,
        DnsRewriteTargetKindEnum kind,
        Guid? excludeId,
        CancellationToken stoppingToken
    )
    {
        string normalizedDomain = domain.TrimEnd('.').ToLowerInvariant();

        IReadOnlyList<DnsRewriteEntity> all = await rewriteRepository.ListAsync(
            stoppingToken: stoppingToken
        );

        return all.FirstOrDefault(rewrite =>
            rewrite.Id != excludeId
            && rewrite.Domain.TrimEnd('.').ToLowerInvariant() == normalizedDomain
            && DnsRewriteTargetClassifier.TryClassify(
                rewrite.Target,
                out DnsRewriteTargetKindEnum existingKind,
                out _
            )
            && existingKind == kind
        );
    }

    /// <summary>
    ///     This method converts a rewrite entity into its REST representation, computing
    ///     <see cref="DnsRewriteDetail.TargetKind" /> fresh via the real
    ///     <see cref="DnsRewriteTargetClassifier" /> -- a stored target was already validated
    ///     through the exact same classifier at write time, so classifying it again here should
    ///     never actually fail; the <see langword="null" /> fallback exists only to keep a read
    ///     endpoint from ever throwing over a row some other, non-REST write path may have
    ///     inserted without this controller's own validation.
    /// </summary>
    private static DnsRewriteDetail ToDetail(DnsRewriteEntity rewrite)
    {
        DnsRewriteTargetKindEnum? targetKind = DnsRewriteTargetClassifier.TryClassify(
            rewrite.Target,
            out DnsRewriteTargetKindEnum kind,
            out _
        )
            ? kind
            : null;

        return new DnsRewriteDetail(
            rewrite.Id,
            rewrite.Domain,
            rewrite.Target,
            targetKind,
            rewrite.IsDisabled,
            rewrite.Comments,
            rewrite.CreatedAtUtc,
            rewrite.UpdatedAtUtc,
            rewrite.OriginatingNodeId,
            rewrite.ClientId,
            rewrite.ClientGroupId
        );
    }
}
