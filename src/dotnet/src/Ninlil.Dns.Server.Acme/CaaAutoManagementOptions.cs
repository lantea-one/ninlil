using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This class binds to the <c>DnsServer:Acme:CaaAutoManagement</c> section of the
///     application's configuration and controls the CAA auto-management feature's own global
///     defaults -- reconciling a <see cref="ZoneTypeEnum.Primary" />/
///     <see cref="ZoneTypeEnum.CloudPrimary" /> zone's CAA record set to match its own configured
///     issuer domain whenever that zone's ACME enrollment or CA selection changes (see
///     <see cref="Caa.CaaRecordReconciliationService" /> for the actual reconciliation logic).
///     Every setting here is a *default*, overridable per zone via
///     <c>ZoneEntity.AutoManageCaaRecords</c>/<c>ZoneEntity.CaaIssuerDomain</c> -- this class
///     supplies the value only when a zone's own field is <see langword="null" />.
/// </summary>
public class CaaAutoManagementOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = $"{AcmeOptions.SectionName}:CaaAutoManagement";

    /// <summary>
    ///     This property contains the global default for whether a zone's CAA record set is
    ///     automatically reconciled, used whenever a zone's own
    ///     <c>ZoneEntity.AutoManageCaaRecords</c> is <see langword="null" />. Defaults
    ///     <see langword="true" /> -- deliberately different from this project's usual
    ///     "capability-expanding toggles default off" posture:
    ///     a zone must already have explicitly opted into ACME (a real, explicit opt-in with real
    ///     consequences of its own) before CAA reconciliation ever does anything for it, so this
    ///     setting is a pure correctness/availability improvement in service of a choice already
    ///     made, the same "self-maintaining, no new attack surface" bucket invariant #6 already
    ///     puts DNSSEC validation and RFC 5011 rollover in -- not a new capability a zone wasn't
    ///     already exposed to.
    /// </summary>
    public bool DefaultEnabled { get; set; } = true;

    /// <summary>
    ///     This property contains the global default CA issuer domain (for example,
    ///     <c>letsencrypt.org</c>) written into a CAA record's own <c>issue</c>/<c>issuewild</c>
    ///     tag value, used whenever a zone's own <c>ZoneEntity.CaaIssuerDomain</c> is
    ///     <see langword="null" />. Deliberately requires an operator to set this explicitly
    ///     (hence the empty default) rather than deriving it from <see cref="AcmeOptions.DirectoryUri" />
    ///     -- see that per-zone field's own doc comment on <c>ZoneEntity</c> for why a
    ///     URL-parsing heuristic was rejected. An empty value here, combined with
    ///     <see cref="DefaultEnabled" /> being <see langword="true" />, means
    ///     <see cref="Caa.CaaRecordReconciliationService" /> treats "enabled but no issuer domain
    ///     resolved" as a no-op, not an error.
    /// </summary>
    public string DefaultIssuerDomain { get; set; } = string.Empty;
}
