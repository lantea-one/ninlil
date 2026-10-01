using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This class binds to the <c>DnsServer:Acme:CaaAutoManagement</c> section of the
///     application's configuration and holds the global defaults of CAA auto-management, which
///     keeps the CAA records of a <see cref="ZoneTypeEnum.Primary" /> or
///     <see cref="ZoneTypeEnum.CloudPrimary" /> zone in line with its issuer domain (see
///     <see cref="Caa.CaaRecordReconciliationService" />). A zone overrides them with
///     <c>ZoneEntity.AutoManageCaaRecords</c> and <c>ZoneEntity.CaaIssuerDomain</c>; these values
///     apply when the zone's field is <see langword="null" /> (or, for the issuer, empty).
/// </summary>
public class CaaAutoManagementOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = $"{AcmeOptions.SectionName}:CaaAutoManagement";

    /// <summary>
    ///     This property contains the global default for whether a zone's CAA record set is
    ///     automatically reconciled, used when a zone's <c>ZoneEntity.AutoManageCaaRecords</c> is
    ///     <see langword="null" />. It defaults to <see langword="true" />, unlike most feature
    ///     switches: nothing is written until an issuer domain is set, globally in
    ///     <see cref="DefaultIssuerDomain" /> or per zone, so it adds no capability and only keeps
    ///     records consistent, like DNSSEC validation and RFC 5011 rollover.
    /// </summary>
    public bool DefaultEnabled { get; set; } = true;

    /// <summary>
    ///     This property contains the global default CA issuer domain (for example,
    ///     <c>letsencrypt.org</c>) written as the value of the CAA <c>issue</c> tag, used when a
    ///     zone's <c>ZoneEntity.CaaIssuerDomain</c> is empty. The operator must set it; it is not
    ///     derived from <see cref="AcmeOptions.DirectoryUri" /> (see <c>ZoneEntity.CaaIssuerDomain</c>
    ///     for why). While it is empty, a zone with no issuer of its own is skipped, not treated
    ///     as an error.
    /// </summary>
    public string DefaultIssuerDomain { get; set; } = string.Empty;
}
