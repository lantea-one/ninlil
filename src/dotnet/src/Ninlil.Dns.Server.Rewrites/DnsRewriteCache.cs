using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.Rewrites;

/// <summary>
///     This class holds the enabled <see cref="DnsRewriteEntity" /> rows, ready to match, in
///     memory, so <see cref="DnsRewriteEngine" /> reads no database rows while the copy is fresh.
///     It is one <see cref="RefreshingSnapshot{T}" /> of a <see cref="DnsRewriteSet" />: the rows
///     are sorted once into the order the engine breaks ties in, split into exact and wildcard
///     rewrites, and each wildcard's regex is compiled once.
///     <para>
///         <strong>Expiry.</strong> The cache subscribes to <see cref="IEntityChangeNotifier" /> and
///         expires the snapshot when a <see cref="DnsRewriteEntity" /> is saved, so a rewrite added,
///         changed, disabled or removed through the API applies to the very next query in this
///         process. A change made elsewhere (another cluster node, a direct database edit) shows
///         after <see cref="SnapshotTimeToLive" />. A failed reload keeps the last good copy and is
///         logged; if the first load fails the copy is empty (nothing is rewritten) until the next
///         time-to-live expiry.
///     </para>
///     <para>
///         <strong>Memory.</strong> A rewrite is a few strings and, for a wildcard, one compiled
///         regex; a thousand rewrites take well under a megabyte.
///     </para>
///     <para>
///         <strong>Storage.</strong> When a read replica is configured, a reload that only follows
///         the age of the copy reads the rewrites from the replica
///         (<see cref="SnapshotLoadReasonEnum.TimeToLive" />). The first load and every reload after a
///         local change read the primary, so a change saved here is never lost to a replica that
///         has not received it yet (see <see cref="SnapshotLoadScopeExtension" />).
///     </para>
/// </summary>
public sealed class DnsRewriteCache : IDisposable
{
    /// <summary>
    ///     This field is how long a copy is used before a background refresh starts. It also
    ///     bounds how long a change made outside this process takes to apply.
    /// </summary>
    public static readonly TimeSpan SnapshotTimeToLive = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     This field is the in-memory copy of the enabled rewrites.
    /// </summary>
    private readonly RefreshingSnapshot<DnsRewriteSet> _snapshot;

    /// <summary>
    ///     This field is the change-notification subscription, removed in <see cref="Dispose" />.
    /// </summary>
    private readonly IDisposable _subscription;

    /// <summary>
    ///     This constructor builds the cache and subscribes it to rewrite changes.
    /// </summary>
    /// <param name="scopeFactory">The factory used to reach the rewrite repository, once per load.</param>
    /// <param name="notifier">The signal that tells this cache when rewrites were saved.</param>
    /// <param name="logger">The logger load failures and unusable rewrites are recorded to.</param>
    /// <param name="timeProvider">The clock for the copy's age; the system clock when <see langword="null" />.</param>
    public DnsRewriteCache(
        IServiceScopeFactory scopeFactory,
        IEntityChangeNotifier notifier,
        ILogger<DnsRewriteCache> logger,
        TimeProvider timeProvider = null
    )
    {
        _snapshot = new RefreshingSnapshot<DnsRewriteSet>(
            (reason, token) => LoadAsync(scopeFactory, logger, reason, token),
            DnsRewriteSet.Empty,
            SnapshotTimeToLive,
            timeProvider ?? TimeProvider.System,
            logger,
            "DNS rewrites"
        );

        // The callback only marks the copy out of date; the next read reloads it.
        _subscription = notifier.Subscribe([typeof(DnsRewriteEntity)], _ => _snapshot.Expire());
    }

    /// <summary>
    ///     This method returns the current rewrites. The first call waits for the first load.
    /// </summary>
    /// <param name="stoppingToken">The token used to stop waiting for a load.</param>
    public ValueTask<DnsRewriteSet> GetAsync(CancellationToken stoppingToken = default) =>
        _snapshot.GetAsync(stoppingToken);

    /// <summary>
    ///     This method marks the copy out of date; the next read waits for a reload. Code that
    ///     changes rewrites without a repository save calls it.
    /// </summary>
    public void Expire() => _snapshot.Expire();

    /// <summary>
    ///     This method removes the change-notification subscription.
    /// </summary>
    public void Dispose() => _subscription.Dispose();

    /// <summary>
    ///     This method reads the enabled rewrites in a short-lived scope and builds the set.
    /// </summary>
    private static async Task<DnsRewriteSet> LoadAsync(
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        SnapshotLoadReasonEnum reason,
        CancellationToken stoppingToken
    )
    {
        await using AsyncServiceScope scope = scopeFactory.CreateLoadScope(reason);
        IReadOnlyList<DnsRewriteEntity> rows = await scope
            .ServiceProvider.GetRequiredService<IDnsRewriteRepository>()
            .ListEnabledAsync(stoppingToken);

        // Sorted by audience, then creation time, so the first hit of a scan is the tie-break
        // winner (see DnsRewriteEngine).
        List<DnsCompiledRewrite> exact = [];
        List<DnsCompiledRewrite> wildcards = [];

        foreach (
            DnsRewriteEntity row in rows.OrderByDescending(ScopeSpecificityRank)
                .ThenBy(row => row.CreatedAtUtc)
        )
        {
            if (string.IsNullOrEmpty(row.Domain))
                continue;

            // The REST controllers reject unclassifiable targets on write, so this only guards
            // against rows written some other way. Such a row never matches.
            if (
                !DnsRewriteTargetClassifier.TryClassify(
                    row.Target,
                    out DnsRewriteTargetKindEnum kind,
                    out _
                )
            )
            {
                logger.LogWarning(
                    "DNS rewrite '{Domain}' ({Id}) has an unclassifiable target; skipping.",
                    row.Domain,
                    row.Id
                );
                continue;
            }

            string normalized = DnsRewriteEngine.NormalizeDomain(row.Domain);
            bool isWildcard = row.Domain.Contains('*');
            Regex regex = null;

            if (isWildcard)
            {
                regex = DnsRewriteEngine.CompileOrNull(
                    DnsRewriteEngine.BuildWildcardRegexPattern(normalized)
                );

                if (regex is null)
                {
                    logger.LogWarning(
                        "A DNS rewrite has a wildcard domain that failed to compile; treating as a non-match. Domain: {Domain}",
                        row.Domain
                    );
                    continue;
                }
            }

            DnsCompiledRewrite compiled = new(
                row.Id,
                row.Domain,
                normalized,
                row.Target,
                kind,
                row.ClientId,
                row.ClientGroupId,
                regex
            );

            (isWildcard ? wildcards : exact).Add(compiled);
        }

        return new DnsRewriteSet(
            exact
                .GroupBy(item => item.NormalizedDomain, StringComparer.Ordinal)
                .ToDictionary(
                    grouping => grouping.Key,
                    grouping => (IReadOnlyList<DnsCompiledRewrite>)[.. grouping],
                    StringComparer.Ordinal
                ),
            wildcards
        );
    }

    /// <summary>
    ///     This method ranks a rewrite's audience; higher is narrower. A row with both a client and
    ///     a group (which the write paths never produce) counts as client-scoped.
    /// </summary>
    private static int ScopeSpecificityRank(DnsRewriteEntity rewrite) =>
        rewrite.ClientId is not null ? 2
        : rewrite.ClientGroupId is not null ? 1
        : 0;
}

/// <summary>
///     This class is one immutable copy of the enabled rewrites, in tie-break order: client-scoped
///     before group-scoped before global, then earliest-created first. <see cref="DnsRewriteEngine" />
///     scans it to answer a query.
/// </summary>
/// <param name="Exact">The rewrites without a <c>*</c>, by lowercase domain without a trailing dot.</param>
/// <param name="Wildcards">The rewrites with a <c>*</c>, each with its compiled regex.</param>
public sealed record DnsRewriteSet(
    IReadOnlyDictionary<string, IReadOnlyList<DnsCompiledRewrite>> Exact,
    IReadOnlyList<DnsCompiledRewrite> Wildcards
)
{
    /// <summary>
    ///     This field is the set used before the first load succeeds: no rewrites.
    /// </summary>
    public static readonly DnsRewriteSet Empty = new(
        new Dictionary<string, IReadOnlyList<DnsCompiledRewrite>>(),
        []
    );

    /// <summary>
    ///     This property says whether no rewrite is enabled, so a query needs no matching.
    /// </summary>
    public bool IsEmpty => Exact.Count == 0 && Wildcards.Count == 0;
}

/// <summary>
///     This record is one enabled rewrite as the engine matches it.
/// </summary>
/// <param name="Id">The row's identifier, for log messages.</param>
/// <param name="Domain">The domain as stored, for log messages.</param>
/// <param name="NormalizedDomain">The domain in lowercase without a trailing dot.</param>
/// <param name="Target">The stored target (an address or a domain name).</param>
/// <param name="Kind">What <paramref name="Target" /> is, which decides the record types it answers.</param>
/// <param name="ClientId">The client the rewrite is scoped to, or <see langword="null" />.</param>
/// <param name="ClientGroupId">The group the rewrite is scoped to, or <see langword="null" />.</param>
/// <param name="Wildcard">The compiled pattern of a wildcard domain, or <see langword="null" /> for an exact one.</param>
public sealed record DnsCompiledRewrite(
    Guid Id,
    string Domain,
    string NormalizedDomain,
    string Target,
    DnsRewriteTargetKindEnum Kind,
    Guid? ClientId,
    Guid? ClientGroupId,
    Regex Wildcard
);
