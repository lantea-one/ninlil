// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Caa;

/// <summary>
///     This record is the outcome of one CAA reconciliation pass, whether triggered automatically
///     off <see cref="Ninlil.Dns.Server.Core.Events.DnsZoneChangedEvent" /> via
///     <see cref="CaaRecordReconciliationEventSubscriber" /> (which discards it), right after a
///     certificate issuance/renewal via <see cref="AcmeCertificateIssuer" /> (which also discards
///     it), or on demand via <c>POST /api/zones/{id}/reconcile-caa</c> (<c>Ninlil.Web</c>'s own
///     <c>ZonesController.ReconcileCaa</c>, which reports it to the caller) -- gives an operator
///     real, useful feedback about what an on-demand reconciliation actually did, or why it did
///     nothing at all, rather than a bare <c>204 No Content</c>.
/// </summary>
/// <param name="RecordsAdded">How many CAA records this pass created.</param>
/// <param name="RecordsRemoved">How many CAA records this pass removed as no longer desired.</param>
/// <param name="Skipped">
///     Whether this pass did no work at all because the zone was not found, is not eligible for
///     CAA reconciliation, has the feature disabled, or has no issuer domain resolved -- distinct
///     from a pass that ran but found nothing to change (that case reports <see langword="false" />
///     here with zero counts, a legitimate "already up to date" outcome).
/// </param>
/// <param name="SkipReason">
///     A human-readable explanation, populated only when <see cref="Skipped" /> is
///     <see langword="true" />.
/// </param>
public sealed record CaaReconciliationResult(
    int RecordsAdded,
    int RecordsRemoved,
    bool Skipped,
    string SkipReason
);
