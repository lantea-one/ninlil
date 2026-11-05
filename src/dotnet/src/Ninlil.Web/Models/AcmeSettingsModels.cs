// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the ACME certificate settings (configuration section
///     <c>DnsServer:Acme</c>) that <c>GET /api/acme-settings</c> returns. The server-wide CAA
///     defaults (configuration section <c>DnsServer:Acme:CaaAutoManagement</c>) are nested under
///     <c>caaAutoManagement</c>; the per-zone override lives on the zone itself
///     (<c>/api/zones</c>). The ACME account's private key is never exposed over REST, so a
///     <c>PATCH</c> to the same endpoint accepts the same fields this record has.
/// </summary>
public record AcmeSettingsDetail(
    bool Enable,
    Uri DirectoryUri,
    IReadOnlyList<string> ContactEmails,
    IReadOnlyList<string> Domains,
    TimeSpan RenewBeforeExpiration,
    TimeSpan PollInterval,
    string AccountStateFilePath,
    string CertificateDirectoryPath,
    string CsrOrganization,
    CaaAutoManagementSettingsDetail CaaAutoManagement
);

/// <summary>
///     This record is the REST shape of the server-wide CAA auto-management defaults
///     (configuration section <c>DnsServer:Acme:CaaAutoManagement</c>), nested in
///     <c>AcmeSettingsDetail.caaAutoManagement</c>.
/// </summary>
public record CaaAutoManagementSettingsDetail(bool DefaultEnabled, string DefaultIssuerDomain);

/// <summary>
///     This record is one row of the certificate inventory that
///     <c>GET /api/acme-settings/certificates</c> returns: one per domain in
///     <c>DnsServer:Acme:Domains</c>, with what the server has recorded about it.
///     <c>hasCertificate</c> separates a domain that has never been issued a certificate
///     from one that simply is not due for renewal yet, whatever <c>isDueForRenewal</c>
///     says.
/// </summary>
/// <param name="Domain">The domain as configured in <c>DnsServer:Acme:Domains</c>.</param>
/// <param name="HasCertificate">
///     <see langword="true" /> once the domain has been issued at least one certificate;
///     <see langword="false" /> means none has ever been issued.
/// </param>
/// <param name="IssuedAtUtc">When the current certificate was issued, or <see langword="null" /> if <c>hasCertificate</c> is <see langword="false" />.</param>
/// <param name="ExpiresAtUtc">When the current certificate expires, or <see langword="null" /> if <c>hasCertificate</c> is <see langword="false" />.</param>
/// <param name="IsDueForRenewal">
///     <see langword="true" /> when the domain has no certificate yet, or its certificate expires
///     within the configured <c>renewBeforeExpiration</c> setting. The certificate
///     renewal job uses the same check.
/// </param>
/// <param name="LastFailureAtUtc">
///     When the domain's most recent issuance or renewal attempt failed, or
///     <see langword="null" /> if that attempt succeeded or none has been made.
/// </param>
/// <param name="LastFailureMessage">
///     A short description of why the most recent attempt failed, or <see langword="null" />
///     together with <c>lastFailureAtUtc</c>.
/// </param>
public record AcmeCertificateInventoryEntry(
    string Domain,
    bool HasCertificate,
    DateTimeOffset? IssuedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    bool IsDueForRenewal,
    DateTimeOffset? LastFailureAtUtc,
    string LastFailureMessage
);
