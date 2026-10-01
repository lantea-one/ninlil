// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Caa;

/// <summary>
///     This record is the outcome of one CAA reconciliation pass. Passes started by
///     <see cref="CaaRecordReconciliationEventSubscriber" /> (on
///     <see cref="Ninlil.Dns.Server.Core.Events.DnsZoneChangedEvent" />) and by
///     <see cref="AcmeCertificateIssuer" /> discard it; <c>ZonesController.ReconcileCaa</c>
///     (<c>POST /api/zones/{id}/reconcile-caa</c>) returns it, so an operator sees what the pass
///     did or why it did nothing.
/// </summary>
/// <param name="RecordsAdded">How many CAA records this pass created.</param>
/// <param name="RecordsRemoved">How many CAA records this pass removed as no longer desired.</param>
/// <param name="Skipped">
///     Whether the pass was skipped because the zone was not found, is not eligible, has the
///     feature off, or has no issuer domain. A pass that ran and found nothing to change reports
///     <see langword="false" /> with zero counts, as does one that could not write because the
///     zone has no SOA yet.
/// </param>
/// <param name="SkipReason">
///     A human-readable reason, set only when <see cref="Skipped" /> is
///     <see langword="true" />.
/// </param>
public sealed record CaaReconciliationResult(
    int RecordsAdded,
    int RecordsRemoved,
    bool Skipped,
    string SkipReason
);
