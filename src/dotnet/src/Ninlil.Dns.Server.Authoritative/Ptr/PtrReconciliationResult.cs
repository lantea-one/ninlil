// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Ptr;

/// <summary>
///     This record is the result of one PTR sync pass (<see cref="PtrSyncService" />). The
///     event-driven pass (<see cref="PtrSyncEventSubscriber" />) discards it;
///     <c>POST /api/zones/{id}/reconcile-ptr</c> (<c>ZonesController.ReconcilePtr</c>) returns it,
///     so the operator sees what the pass did or why it did nothing.
/// </summary>
/// <param name="RecordsAdded">
///     How many PTR and CNAME records the pass created, across all reverse zones and, for RFC
///     2317 classless zones, the enclosing /24 zones.
/// </param>
/// <param name="RecordsRemoved">The same, for records removed as no longer desired.</param>
/// <param name="ReverseZonesTouched">
///     How many zones (reverse zones and, for classless zones, the enclosing /24 zones) the
///     pass created, wrote to or checked for stale records.
/// </param>
/// <param name="Skipped">
///     Whether the pass did nothing because the forward zone was missing, not eligible, or has
///     PTR sync off. A pass that ran and found nothing to change reports
///     <see langword="false" /> with zero counts.
/// </param>
/// <param name="SkipReason">
///     The reason, set only when <see cref="Skipped" /> is <see langword="true" />.
/// </param>
public sealed record PtrReconciliationResult(
    int RecordsAdded,
    int RecordsRemoved,
    int ReverseZonesTouched,
    bool Skipped,
    string SkipReason
);
