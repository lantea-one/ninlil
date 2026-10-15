using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Presets;
using Ninlil.Dns.Server.Rewrites;
using Ninlil.Dns.Server.Rewrites.SafeSearch;
using Ninlil.Web.ClusterSync;
using Ninlil.Web.Examples.SafeSearch;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Ninlil.Web.Routing;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller serves the Safe Search catalog (<c>SafeSearchCatalog</c>): one-click
///     toggles that force each major search engine's published DNS-based safe-search mechanism.
///     It is a thin layer over the DNS rewrites of <see cref="DnsRewritesController" />: enabling
///     an entry creates (or re-enables) one CNAME <see cref="DnsRewriteEntity" /> per hostname
///     mapping, as an operator could by hand. <c>SafeSearchCatalog</c> documents each mapping's
///     source and how it relates to the <see cref="DnsRewriteOptions.Enable" /> switch. Reads need
///     <c>dnsrewrites:read</c> and changes need <c>dnsrewrites:write</c>, the rewrite permissions.
///     Enabling an entry also turns <see cref="DnsRewriteOptions.Enable" /> on if it was off,
///     without requiring <c>rewritesettings:write</c>: that switch only decides whether rewrites,
///     including the ones just written, are used at all. Disabling never turns it off, since
///     other rewrites may need it.
///     <para>
///         <b>Scoping to a client or client group.</b> Every action takes optional, mutually
///         exclusive <c>clientId</c> and <c>clientGroupId</c> query parameters. Without them the
///         actions work on global (unscoped) rewrites. With one, <c>List</c> reports whether the
///         entry is enabled for that target, <c>Enable</c> creates or re-enables rewrites scoped to
///         it (through <see cref="DnsRewriteEntity.ClientId" /> or <c>ClientGroupId</c>; see that
///         entity for the query-time behavior), and <c>Disable</c> removes only that target's
///         rewrites. A scoped rewrite and a global one for the same mapping are separate rows; both
///         can exist and each applies to its own audience.
///     </para>
///     <para>
///         <b>Cluster replication.</b> Only global rewrites replicate: creating or re-enabling one
///         stamps this node's id on it and publishes a <see cref="DnsRewriteChangedEvent" />, and
///         removing one publishes the same event with <c>IsDeleted</c>, which
///         <c>RewriteClusterSyncEventSubscriber</c> sends to the peers. A rewrite scoped to a
///         client or client group stays on this node and publishes nothing: the cluster payload
///         has no scope, and clients and groups exist only on the node that made them, so a peer
///         would otherwise create a global rewrite. The <see cref="DnsRewriteOptions.Enable" />
///         switch is different: it is a cluster-synced setting, so turning it on here, for any
///         scope, also turns it on for every peer through <see cref="ClusterConfigSyncSettingsRecorder" />.
///         Peers then apply their own rewrites, and the global Safe Search rewrites that
///         replicate to them.
///     </para>
/// </summary>
/// <param name="rewriteRepository">Reads and writes rewrites.</param>
/// <param name="rewriteOptions">The current rewrite settings, checked before turning the engine on.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file <see cref="DnsRewriteOptions.Enable" /> is written to when enabling an entry turns it on.</param>
/// <param name="unitOfWork">Commits staged changes.</param>
/// <param name="clientRepository">Confirms a <c>clientId</c> target exists.</param>
/// <param name="clientGroupRepository">Confirms a <c>clientGroupId</c> target exists.</param>
/// <param name="eventPublisher">
///     Publishes <see cref="DnsRewriteChangedEvent" /> for global rewrites so the cluster-sync
///     subscriber sends them to every peer.
/// </param>
/// <param name="clusterOptions">
///     Supplies this node's cluster node id, stamped onto
///     <c>DnsRewriteEntity.OriginatingNodeId</c> on a global rewrite that is created or re-enabled.
/// </param>
/// <param name="clusterSyncRecorder">
///     Records the <see cref="DnsRewriteOptions.Enable" /> switch and sends it to every cluster
///     peer when enabling an entry turns it on.
/// </param>
[ApiController]
[Route("api/safe-search")]
public class SafeSearchController(
    IDnsRewriteRepository rewriteRepository,
    IOptionsMonitor<DnsRewriteOptions> rewriteOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions,
    IUnitOfWork unitOfWork,
    IDnsClientRepository clientRepository,
    IDnsClientGroupRepository clientGroupRepository,
    IDnsServerEventPublisher eventPublisher,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ClusterConfigSyncSettingsRecorder clusterSyncRecorder
) : ControllerBase
{
    /// <summary>
    ///     This action lists every catalog entry and whether it is fully enabled (every hostname
    ///     mapping present as an enabled rewrite), globally or for the given <c>clientId</c> or
    ///     <c>clientGroupId</c> (not both, which returns 400). Requires <c>dnsrewrites:read</c>.
    /// </summary>
    [HttpGet]
    [ReadFromReplica]
    [RequirePermission("dnsrewrites:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(SafeSearchListExample))]
    public async Task<ActionResult<IReadOnlyList<SafeSearchDetail>>> List(
        [FromQuery] Guid? clientId,
        [FromQuery] Guid? clientGroupId,
        CancellationToken stoppingToken
    )
    {
        ValidateMutuallyExclusive(clientId, clientGroupId);

        IReadOnlyList<DnsRewriteEntity> rewrites = FilterByScope(
            await rewriteRepository.ListAsync(stoppingToken: stoppingToken),
            clientId,
            clientGroupId
        );

        return Ok(SafeSearchCatalog.Entries.Select(entry => ToDetail(entry, rewrites)).ToList());
    }

    /// <summary>
    ///     This action enables one catalog entry: for each hostname mapping it creates a rewrite in
    ///     the requested scope, or re-enables a disabled one. It also turns the rewrite engine on
    ///     if it was off, whatever the scope, and replicates that switch to the cluster peers.
    ///     Returns 400 when both targets are given or the target
    ///     does not exist, 404 for an unknown key, and 409, with nothing written, when a mapping's
    ///     hostname already has a different CNAME rewrite in the same scope; every mapping is
    ///     checked before anything is written. Global rewrites that are created or re-enabled are
    ///     replicated to cluster peers; scoped ones are not. Requires <c>dnsrewrites:write</c>.
    /// </summary>
    [HttpPost("{key}")]
    [RequirePermission("dnsrewrites:write")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(SafeSearchDetailExample))]
    public async Task<ActionResult<SafeSearchDetail>> Enable(
        string key,
        [FromQuery] Guid? clientId,
        [FromQuery] Guid? clientGroupId,
        CancellationToken stoppingToken
    )
    {
        SafeSearchCatalogEntry entry = SafeSearchCatalog.FindByKey(key);
        if (entry is null)
            throw new NotFoundException("SafeSearchEngine", key);

        await ValidateTargetAsync(clientId, clientGroupId, stoppingToken);

        IReadOnlyList<DnsRewriteEntity> rewrites = FilterByScope(
            await rewriteRepository.ListAsync(stoppingToken: stoppingToken),
            clientId,
            clientGroupId
        );

        // Check every mapping before writing anything, so a rejected request never leaves the entry
        // half applied. Only rewrites in the same scope are considered: a global rewrite elsewhere
        // for the hostname does not block a client-scoped Safe Search override, or the reverse.
        foreach (SafeSearchRewriteMapping mapping in entry.Mappings)
        {
            DnsRewriteEntity conflicting = FindConflictingCname(rewrites, mapping);
            if (conflicting is not null)
                throw new ConflictException(
                    $"'{mapping.SourceDomain}' already has an unrelated CNAME rewrite pointing to "
                        + $"'{conflicting.Target}'; enabling '{entry.DisplayName}' Safe Search "
                        + $"would need it to point to '{mapping.TargetDomain}' instead. Remove or "
                        + "change the existing rewrite first."
                );
        }

        bool isGlobal = clientId is null && clientGroupId is null;
        List<Guid> changedIds = [];

        foreach (SafeSearchRewriteMapping mapping in entry.Mappings)
        {
            DnsRewriteEntity existing = FindExactMatch(rewrites, mapping);

            if (existing is not null)
            {
                if (existing.IsDisabled)
                {
                    existing.IsDisabled = false;
                    existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    if (isGlobal)
                        existing.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;
                    rewriteRepository.Update(existing);
                    changedIds.Add(existing.Id);
                }

                continue;
            }

            DnsRewriteEntity created = new()
            {
                Id = Guid.NewGuid(),
                Domain = mapping.SourceDomain,
                Target = mapping.TargetDomain,
                IsDisabled = false,
                Comments =
                    $"Managed by Ninlil's Safe Search catalog (key: {entry.Key}). Source: "
                    + $"{entry.SourceDocumentationUrl}.",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ClientId = clientId,
                ClientGroupId = clientGroupId,
                OriginatingNodeId = isGlobal ? clusterOptions.CurrentValue.NodeId : null,
            };

            await rewriteRepository.AddAsync(created, stoppingToken);
            changedIds.Add(created.Id);
        }

        await unitOfWork.SaveChangesAsync(stoppingToken);

        // Scoped rewrites stay on this node (see the class summary).
        if (isGlobal)
            foreach (Guid changedId in changedIds)
                eventPublisher.Publish(
                    new DnsRewriteChangedEvent(DateTimeOffset.UtcNow, changedId)
                );

        // The engine switch gates every rewrite whatever its scope, so a scoped rewrite is as inert
        // as a global one while it is off; the check does not depend on clientId or clientGroupId.
        if (!rewriteOptions.CurrentValue.Enable)
        {
            List<PresetConfigurationSetting> engineSwitch =
            [
                new PresetConfigurationSetting(
                    DnsRewriteOptions.SectionName,
                    nameof(DnsRewriteOptions.Enable),
                    true
                ),
            ];

            await PresetConfigurationFileApplier.ApplyAsync(
                engineSwitch,
                runtimeConfigurationOptions.ConfigurationFilePath
            );

            // The switch is a cluster-synced setting, so enabling Safe Search anywhere means
            // "rewrites are used" everywhere. The peers apply it to their own files.
            await clusterSyncRecorder.RecordAndPublishAsync(
                DnsRewriteOptions.SectionName,
                engineSwitch,
                stoppingToken
            );
        }

        IReadOnlyList<DnsRewriteEntity> refreshed = FilterByScope(
            await rewriteRepository.ListAsync(stoppingToken: stoppingToken),
            clientId,
            clientGroupId
        );

        return Ok(ToDetail(entry, refreshed));
    }

    /// <summary>
    ///     This action disables one catalog entry: it removes every rewrite in the requested scope
    ///     whose domain and target match one of the entry's mappings, including an identical one an
    ///     operator configured by hand, and leaves other scopes alone. It does not turn the rewrite
    ///     engine off. Having nothing to remove, or naming a target that does not exist, is not an
    ///     error. Removing global rewrites tells the cluster peers to delete them too; removing
    ///     scoped ones does not. Returns 400 when both targets are given and 404 for an unknown
    ///     key. Requires <c>dnsrewrites:write</c>.
    /// </summary>
    [HttpDelete("{key}")]
    [RequirePermission("dnsrewrites:write")]
    public async Task<IActionResult> Disable(
        string key,
        [FromQuery] Guid? clientId,
        [FromQuery] Guid? clientGroupId,
        CancellationToken stoppingToken
    )
    {
        SafeSearchCatalogEntry entry = SafeSearchCatalog.FindByKey(key);
        if (entry is null)
            throw new NotFoundException("SafeSearchEngine", key);

        ValidateMutuallyExclusive(clientId, clientGroupId);

        IReadOnlyList<DnsRewriteEntity> rewrites = FilterByScope(
            await rewriteRepository.ListAsync(stoppingToken: stoppingToken),
            clientId,
            clientGroupId
        );

        List<Guid> removedIds = [];

        foreach (SafeSearchRewriteMapping mapping in entry.Mappings)
        {
            DnsRewriteEntity existing = FindExactMatch(rewrites, mapping);
            if (existing is null)
                continue;

            rewriteRepository.Remove(existing);
            removedIds.Add(existing.Id);
        }

        await unitOfWork.SaveChangesAsync(stoppingToken);

        // Scoped rewrites stay on this node (see the class summary).
        if (clientId is null && clientGroupId is null)
            foreach (Guid removedId in removedIds)
                eventPublisher.Publish(
                    new DnsRewriteChangedEvent(DateTimeOffset.UtcNow, removedId, IsDeleted: true)
                );

        return NoContent();
    }

    /// <summary>
    ///     This method rejects a request that names both a client and a client group.
    /// </summary>
    private static void ValidateMutuallyExclusive(Guid? clientId, Guid? clientGroupId)
    {
        if (clientId is not null && clientGroupId is not null)
            throw new ValidationException("'clientId' and 'clientGroupId' cannot both be given.");
    }

    /// <summary>
    ///     This method checks an <c>Enable</c> request's target: not both kinds at once, and the
    ///     one given must exist, so no scoped rewrite points at a missing client or group.
    /// </summary>
    private async Task ValidateTargetAsync(
        Guid? clientId,
        Guid? clientGroupId,
        CancellationToken stoppingToken
    )
    {
        ValidateMutuallyExclusive(clientId, clientGroupId);

        if (clientId is { } id && await clientRepository.GetByIdAsync(id, stoppingToken) is null)
            throw new ValidationException($"No client with id '{id}' exists.");

        if (
            clientGroupId is { } groupId
            && await clientGroupRepository.GetByIdAsync(groupId, stoppingToken) is null
        )
            throw new ValidationException($"No client group with id '{groupId}' exists.");
    }

    /// <summary>
    ///     This method keeps only the rewrites in one scope: those whose
    ///     <see cref="DnsRewriteEntity.ClientId" /> and <c>ClientGroupId</c> both equal
    ///     <paramref name="clientId" /> and <paramref name="clientGroupId" />. Two nulls select the
    ///     global rows. Every action filters right after loading, so
    ///     <see cref="FindConflictingCname" />, <see cref="FindExactMatch" /> and
    ///     <see cref="ToDetail" /> need no scope handling.
    /// </summary>
    private static IReadOnlyList<DnsRewriteEntity> FilterByScope(
        IReadOnlyList<DnsRewriteEntity> rewrites,
        Guid? clientId,
        Guid? clientGroupId
    ) =>
        [
            .. rewrites.Where(rewrite =>
                rewrite.ClientId == clientId && rewrite.ClientGroupId == clientGroupId
            ),
        ];

    /// <summary>
    ///     This method finds a rewrite for a mapping's
    ///     <see cref="SafeSearchRewriteMapping.SourceDomain" /> whose target is a CNAME other than
    ///     the mapping's <see cref="SafeSearchRewriteMapping.TargetDomain" />: another rewrite
    ///     already claims the hostname. Safe Search mappings are always CNAMEs, so only a CNAME can
    ///     conflict.
    /// </summary>
    private static DnsRewriteEntity FindConflictingCname(
        IReadOnlyList<DnsRewriteEntity> rewrites,
        SafeSearchRewriteMapping mapping
    ) =>
        rewrites.FirstOrDefault(rewrite =>
            NormalizeDomain(rewrite.Domain) == NormalizeDomain(mapping.SourceDomain)
            && !string.Equals(
                rewrite.Target?.TrimEnd('.'),
                mapping.TargetDomain,
                StringComparison.OrdinalIgnoreCase
            )
            && DnsRewriteTargetClassifier.TryClassify(
                rewrite.Target,
                out DnsRewriteTargetKindEnum kind,
                out _
            )
            && kind == DnsRewriteTargetKindEnum.Cname
        );

    /// <summary>
    ///     This method finds a rewrite whose domain and target both match a mapping, whether the
    ///     catalog created it or an operator configured the same override by hand.
    /// </summary>
    private static DnsRewriteEntity FindExactMatch(
        IReadOnlyList<DnsRewriteEntity> rewrites,
        SafeSearchRewriteMapping mapping
    ) =>
        rewrites.FirstOrDefault(rewrite =>
            NormalizeDomain(rewrite.Domain) == NormalizeDomain(mapping.SourceDomain)
            && string.Equals(
                rewrite.Target?.TrimEnd('.'),
                mapping.TargetDomain,
                StringComparison.OrdinalIgnoreCase
            )
        );

    /// <summary>
    ///     This method lowercases a domain and removes a trailing dot, as <c>DnsRewriteEngine</c>
    ///     does.
    /// </summary>
    private static string NormalizeDomain(string value) =>
        value?.TrimEnd('.').ToLowerInvariant() ?? string.Empty;

    /// <summary>
    ///     This method builds <see cref="SafeSearchDetail" /> for an entry; it counts as enabled
    ///     only when every mapping has a matching, enabled rewrite.
    /// </summary>
    private static SafeSearchDetail ToDetail(
        SafeSearchCatalogEntry entry,
        IReadOnlyList<DnsRewriteEntity> rewrites
    )
    {
        bool isEnabled = entry.Mappings.All(mapping =>
            FindExactMatch(rewrites, mapping) is { IsDisabled: false }
        );

        return new SafeSearchDetail(
            entry.Key,
            entry.DisplayName,
            entry.Description,
            entry.SourceDocumentationUrl,
            entry.Mappings.Count,
            isEnabled
        );
    }
}
