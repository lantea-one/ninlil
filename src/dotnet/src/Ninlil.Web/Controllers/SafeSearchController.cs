using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Presets;
using Ninlil.Dns.Server.Rewrites;
using Ninlil.Dns.Server.Rewrites.SafeSearch;
using Ninlil.Web.Examples.SafeSearch;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers Ninlil's own bundled Safe Search / parental-control catalog
///     (<c>SafeSearchCatalog</c>) -- one-click toggles that force each major search engine's own
///     publicly documented DNS-based safe-search mechanism. It is a thin layer over the existing
///     DNS rewrite engine already covered by <see cref="DnsRewritesController" />, not a parallel
///     mechanism: enabling a catalog entry creates (or re-enables) an ordinary, CNAME-classified
///     <see cref="DnsRewriteEntity" /> row for each of that entry's own hostname mappings, exactly
///     what an operator would configure by hand today. See <c>SafeSearchCatalog</c>'s own doc
///     comment for every mapping's real source and this feature's own documented interaction with
///     <see cref="DnsRewriteOptions.Enable" />'s single global toggle. Every action requires the
///     <c>dnsrewrites:read</c> or <c>dnsrewrites:write</c> permission -- the same permissions
///     <see cref="DnsRewritesController" /> itself requires, since this controller only ever
///     creates/removes ordinary <see cref="DnsRewriteEntity" /> rows; enabling an entry
///     additionally flips <see cref="DnsRewriteOptions.Enable" /> on when it was off, which this
///     controller treats as within scope of <c>dnsrewrites:write</c> rather than requiring the
///     separate <c>rewritesettings:write</c> permission too -- a deliberate, narrow exception,
///     since that flag's only effect is whether rewrites (including the very rows this action just
///     wrote) are consulted at all, not a distinct, unrelated capability.
///     <para>
///         <b>Per-client/client-group scoping (optional <c>clientId</c>/<c>clientGroupId</c> query
///         parameters, mutually exclusive, on every action below)</b>: omitting both preserves
///         every one of this controller's own pre-existing, unscoped (global) semantics exactly --
///         <c>List</c> reports whether an entry is globally enabled, <c>Enable</c> creates/
///         re-enables its mapping rows as ordinary, unscoped <see cref="DnsRewriteEntity" /> rows,
///         and <c>Disable</c> removes exactly those unscoped rows. Naming one narrows every action
///         to that single target's own rows instead: <c>List</c> reports whether the entry is
///         enabled <em>for that target specifically</em>; <c>Enable</c> creates/re-enables rows
///         scoped to it (via <see cref="DnsRewriteEntity.ClientId" />/<c>ClientGroupId</c>, see
///         that entity's own doc comment for the resulting query-time behavior); <c>Disable</c>
///         removes only that target's own scoped rows, leaving every global row and every other
///         target's own rows untouched. A scoped row is a genuinely separate row from a global one
///         for the same mapping -- both can coexist, and both independently apply (the scoped row
///         to its one audience, the global row to everyone) once <see cref="DnsRewriteOptions.Enable" />
///         is on, which a scoped <c>Enable</c> still turns on if it was off, for the identical
///         reason an unscoped one already does: a client-scoped rewrite is exactly as unable to
///         ever take effect while the single global engine toggle is off as a global one is.
///     </para>
/// </summary>
/// <param name="rewriteRepository">The repository this controller reads and writes rewrite rows through.</param>
/// <param name="rewriteOptions">This host's own currently-effective rewrite settings, live -- checked before writing to the configuration file.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file <see cref="DnsRewriteOptions.Enable" /> is merged into, when enabling an entry needs to turn it on.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
/// <param name="clientRepository">The repository used to confirm an optional <c>clientId</c> target actually exists.</param>
/// <param name="clientGroupRepository">The repository used to confirm an optional <c>clientGroupId</c> target actually exists.</param>
[ApiController]
[Route("api/safe-search")]
public class SafeSearchController(
    IDnsRewriteRepository rewriteRepository,
    IOptionsMonitor<DnsRewriteOptions> rewriteOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions,
    IUnitOfWork unitOfWork,
    IDnsClientRepository clientRepository,
    IDnsClientGroupRepository clientGroupRepository
) : ControllerBase
{
    /// <summary>
    ///     This action lists every catalog entry this server offers, together with whether each is
    ///     currently fully enabled (every one of its own hostname mappings present as an enabled
    ///     rewrite) -- globally by default, or for a single optional <c>clientId</c>/
    ///     <c>clientGroupId</c> target (mutually exclusive) when named -- see this class's own doc
    ///     comment for the full scoping contract.
    /// </summary>
    [HttpGet]
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
    ///     This action enables a single catalog entry: for every one of its own hostname mappings,
    ///     creates a matching rewrite row if none exists, or re-enables one that was previously
    ///     disabled -- globally by default, or scoped to a single optional <c>clientId</c>/
    ///     <c>clientGroupId</c> target (mutually exclusive) when named, see this class's own doc
    ///     comment. Rejected with <c>400 Bad Request</c> if both are given, or if a named target
    ///     does not actually exist. Rejected with <c>409 Conflict</c>, with nothing at all written,
    ///     if any mapping would collide with an existing, unrelated CNAME-classified rewrite for
    ///     the same hostname <em>within the same scope</em> pointing somewhere else -- validated
    ///     for every mapping before any write is made, so a rejected request never leaves this
    ///     entry partially applied. Also turns <see cref="DnsRewriteOptions.Enable" /> on if it was
    ///     off, regardless of scope -- see this class's own doc comment for why that side effect
    ///     exists and is deliberately not reversed on disable.
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

        // Validate every mapping before writing anything at all -- a rejected request must never
        // leave this entry half-applied (the same "fully validate, then fully apply" discipline
        // BackupController's own restore path already follows). The conflict/exact-match scan is
        // pre-filtered to this same scope only: a global rewrite pointing somewhere else for the
        // same hostname never blocks enabling a client-scoped Safe Search override (and vice
        // versa) -- each scope's own rows are entirely independent.
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

        foreach (SafeSearchRewriteMapping mapping in entry.Mappings)
        {
            DnsRewriteEntity existing = FindExactMatch(rewrites, mapping);

            if (existing is not null)
            {
                if (existing.IsDisabled)
                {
                    existing.IsDisabled = false;
                    existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    rewriteRepository.Update(existing);
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
            };

            await rewriteRepository.AddAsync(created, stoppingToken);
        }

        await unitOfWork.SaveChangesAsync(stoppingToken);

        // The single global engine toggle gates every rewrite regardless of scope -- a
        // client-scoped row created above is exactly as inert as a global one would be while this
        // stays off, so this check and side effect are deliberately unconditional on clientId/
        // clientGroupId.
        if (!rewriteOptions.CurrentValue.Enable)
            await PresetConfigurationFileApplier.ApplyAsync(
                [
                    new PresetConfigurationSetting(
                        DnsRewriteOptions.SectionName,
                        nameof(DnsRewriteOptions.Enable),
                        true
                    ),
                ],
                runtimeConfigurationOptions.ConfigurationFilePath
            );

        IReadOnlyList<DnsRewriteEntity> refreshed = FilterByScope(
            await rewriteRepository.ListAsync(stoppingToken: stoppingToken),
            clientId,
            clientGroupId
        );

        return Ok(ToDetail(entry, refreshed));
    }

    /// <summary>
    ///     This action disables a single catalog entry: removes every rewrite row, within the
    ///     given scope only, that exactly matches one of its own hostname mappings (source domain
    ///     and target both) -- globally by default, or scoped to a single optional <c>clientId</c>/
    ///     <c>clientGroupId</c> target (mutually exclusive) when named, leaving every other scope's
    ///     own rows (global rows, or another target's own rows) completely untouched. Never touches
    ///     <see cref="DnsRewriteOptions.Enable" /> -- see this class's own doc comment for why. A
    ///     no-op, not an error, for a mapping with no matching row in the given scope, or for an
    ///     unrecognized target (nothing in that scope could ever exist to remove).
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

        foreach (SafeSearchRewriteMapping mapping in entry.Mappings)
        {
            DnsRewriteEntity existing = FindExactMatch(rewrites, mapping);
            if (existing is not null)
                rewriteRepository.Remove(existing);
        }

        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method rejects a request naming both a client and a client-group target at once --
    ///     shared by every action that accepts the optional scoping pair.
    /// </summary>
    private static void ValidateMutuallyExclusive(Guid? clientId, Guid? clientGroupId)
    {
        if (clientId is not null && clientGroupId is not null)
            throw new ValidationException("'clientId' and 'clientGroupId' cannot both be given.");
    }

    /// <summary>
    ///     This method validates an <c>Enable</c> request's own optional scoping target: rejects
    ///     both being given at once, then confirms whichever one <em>is</em> given actually names
    ///     an existing row -- a write action must never silently create a scoped rewrite pointed at
    ///     a client/group that does not exist.
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
    ///     This method narrows a rewrite set down to exactly one scope -- every row whose own
    ///     <see cref="DnsRewriteEntity.ClientId" />/<c>ClientGroupId</c> pair matches
    ///     <paramref name="clientId" />/<paramref name="clientGroupId" /> exactly (both
    ///     <see langword="null" /> matches only the global rows, the same "both null" comparison
    ///     works for a genuine single target since at most one of the two parameters is ever
    ///     non-null once <see cref="ValidateMutuallyExclusive" /> has already run). Every method on
    ///     this controller applies this filter immediately after loading rewrites, so
    ///     <see cref="FindConflictingCname" />/<see cref="FindExactMatch" />/<see cref="ToDetail" />
    ///     themselves need no scope awareness of their own at all.
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
    ///     This method finds an existing rewrite row whose own normalized domain matches a
    ///     mapping's <see cref="SafeSearchRewriteMapping.SourceDomain" /> and whose own target
    ///     classifies as CNAME (the only kind that could genuinely conflict with a Safe Search
    ///     mapping, which is always CNAME-classified itself), but whose target text does
    ///     <b>not</b> match the mapping's own <see cref="SafeSearchRewriteMapping.TargetDomain" /> --
    ///     the real "something else already claims this hostname" conflict case.
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
    ///     This method finds an existing rewrite row whose own normalized domain and target both
    ///     already exactly match a mapping -- already satisfies it, whether this catalog created
    ///     it or an operator happened to hand-configure the identical override themselves.
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
    ///     This method normalizes a domain to lowercase with no trailing dot, mirroring
    ///     <c>DnsRewriteEngine</c>'s own identical normalization.
    /// </summary>
    private static string NormalizeDomain(string value) =>
        value?.TrimEnd('.').ToLowerInvariant() ?? string.Empty;

    /// <summary>
    ///     This method builds <see cref="SafeSearchDetail" /> for a catalog entry, considering it
    ///     enabled only when <b>every</b> one of its own mappings has a matching, enabled rewrite
    ///     row.
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
