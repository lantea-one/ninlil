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
///     This controller manages DNS rewrites (<see cref="DnsRewriteEntity" />): simple
///     <c>domain -&gt; target</c> overrides that <c>DnsRewriteEngine</c> in <c>Server.Rewrites</c>
///     applies before zone, forwarder and recursive resolution. The route is flat, like
///     <see cref="ForwardersController" />. Reads need <c>dnsrewrites:read</c> and changes need
///     <c>dnsrewrites:write</c>.
///     <para>
///         <b>A change applies to the next query without any cache reset.</b>
///         <c>DnsRewriteEngine.TryBuildResponseAsync</c> reads the enabled rewrites through
///         <see cref="IDnsRewriteRepository.ListEnabledAsync" /> in a new
///         <see cref="IServiceScope" /> on every query. Every write still publishes a
///         <see cref="DnsRewriteChangedEvent" />, which <c>RewriteClusterSyncEventSubscriber</c>
///         sends to the other cluster nodes.
///     </para>
///     <para>
///         <b>Duplicates.</b> <see cref="DnsRewriteEntity.Domain" /> is not unique in storage,
///         since one domain may have both an A-target and an AAAA-target rewrite. A create or
///         update returns 409 when another rewrite has the same normalized domain and the same
///         classified <see cref="DnsRewriteTargetKindEnum" />, because two rewrites answering the
///         same record kind for one name would be ambiguous.
///     </para>
/// </summary>
/// <param name="rewriteRepository">Reads and writes rewrites.</param>
/// <param name="unitOfWork">Commits staged changes.</param>
/// <param name="eventPublisher">
///     Publishes <see cref="DnsRewriteChangedEvent" /> on every write so
///     <c>RewriteClusterSyncEventSubscriber</c> sends the change to every cluster peer.
/// </param>
/// <param name="clusterOptions">
///     Supplies this node's cluster node id, stamped onto
///     <c>DnsRewriteEntity.OriginatingNodeId</c> on every create and update.
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
    ///     This action lists DNS rewrites, one page at a time, each with its classified target
    ///     kind. Requires <c>dnsrewrites:read</c>.
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

        // The page and the total count are both computed by the storage engine (ORDER
        // BY/OFFSET/LIMIT or the MongoDB equivalent), not by loading every rewrite into memory.
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
    ///     This action returns one DNS rewrite. Returns 404 for an unknown id. Requires
    ///     <c>dnsrewrites:read</c>.
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
    ///     This action creates a DNS rewrite. Returns 400 for an invalid domain or a target that is
    ///     not an IP address or domain name, and 409 when a rewrite of the same kind exists for the
    ///     domain. Requires <c>dnsrewrites:write</c>.
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
    ///     This action replaces a DNS rewrite's fields. Validation and errors are as for
    ///     <see cref="Create" />, plus 404 for an unknown id. Requires <c>dnsrewrites:write</c>.
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
    ///     This action updates a DNS rewrite as an RFC 7396 merge patch: an absent field is
    ///     unchanged, a JSON <c>null</c> clears the comments, and any other value replaces it.
    ///     Changing the domain or target is checked as for <see cref="Create" />. Requires
    ///     <c>dnsrewrites:write</c>.
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
    ///     This action deletes a DNS rewrite. Nothing refers to a rewrite by id, so nothing needs
    ///     cleaning up first. Returns 404 for an unknown id. Requires <c>dnsrewrites:write</c>.
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
    ///     This method checks that a domain is present and a valid DNS name. A <c>*</c> label is
    ///     ordinary label content to
    ///     <see cref="DnsName.TryParse(string, out DnsName, out string)" />, so a wildcard such as
    ///     <c>*.example.com</c> passes without special handling.
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
    ///     This method finds another rewrite with the same normalized domain and the same
    ///     classified target kind, the ambiguous case this controller rejects. It filters the whole
    ///     set loaded through <see cref="IRepository{TEntity}.ListAsync" />, as there is no
    ///     dedicated lookup.
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
    ///     This method converts a rewrite entity to its API representation, classifying the target
    ///     to fill in <see cref="DnsRewriteDetail.TargetKind" />. A target saved through this
    ///     controller always classifies; for a row written another way the kind is left
    ///     <see langword="null" />, so a read never fails.
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
