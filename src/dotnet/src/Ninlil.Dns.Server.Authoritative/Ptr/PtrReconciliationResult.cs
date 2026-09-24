// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Ptr;

/// <summary>
///     This record is the outcome of one PTR/reverse-zone sync reconciliation pass, whether
///     triggered automatically off <see cref="Core.Events.DnsZoneChangedEvent" /> via
///     <see cref="PtrSyncEventSubscriber" /> (which discards it -- that path has no REST caller
///     waiting on a response) or on demand via <c>POST /api/zones/{id}/reconcile-ptr</c>
///     (<c>Ninlil.Web</c>'s own <c>ZonesController.ReconcilePtr</c>) -- gives an operator real,
///     useful feedback about what an on-demand reconciliation actually did, or why it did nothing
///     at all, rather than a bare <c>204 No Content</c>.
/// </summary>
/// <param name="RecordsAdded">
///     How many PTR/CNAME records this pass created -- across every reverse zone and, for a
///     classless (RFC 2317) grouping, every delegating parent zone this forward zone's own
///     A/AAAA record set touches.
/// </param>
/// <param name="RecordsRemoved">The same, for records removed as no longer desired.</param>
/// <param name="ReverseZonesTouched">
///     How many distinct zones (reverse zones and, for a classless grouping, delegating parent
///     zones) this pass created, wrote into, or visited for stale-record cleanup.
/// </param>
/// <param name="Skipped">
///     Whether this pass did no work at all because the forward zone was not found, is not
///     eligible for PTR sync, or has the feature disabled -- distinct from a pass that ran but
///     found nothing to change (that case reports <see langword="false" /> here with zero counts,
///     a legitimate "already up to date" outcome).
/// </param>
/// <param name="SkipReason">
///     A human-readable explanation, populated only when <see cref="Skipped" /> is
///     <see langword="true" />.
/// </param>
public sealed record PtrReconciliationResult(
    int RecordsAdded,
    int RecordsRemoved,
    int ReverseZonesTouched,
    bool Skipped,
    string SkipReason
);
