// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the ACME certificate settings (<c>AcmeOptions</c>) that
///     <c>GET /api/acme-settings</c> returns. The server-wide CAA defaults
///     (<c>CaaAutoManagementOptions</c>) are nested under <see cref="CaaAutoManagement" />; the
///     per-zone override lives on the zone itself (<c>ZonesController</c>). The ACME account's
///     private key is never exposed over REST, so a <c>PATCH</c> to the same endpoint accepts the
///     same fields this record has.
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
///     (<c>CaaAutoManagementOptions</c>), nested in <see cref="AcmeSettingsDetail.CaaAutoManagement" />.
/// </summary>
public record CaaAutoManagementSettingsDetail(bool DefaultEnabled, string DefaultIssuerDomain);

/// <summary>
///     This record is one row of the certificate inventory that
///     <c>AcmeSettingsController.Certificates</c> returns: one per domain in
///     <c>AcmeOptions.Domains</c>, with what <c>IAcmeAccountStore</c> knows about it.
///     <see cref="HasCertificate" /> separates a domain that has never been issued a certificate
///     from one that simply is not due for renewal yet, whatever <see cref="IsDueForRenewal" />
///     says.
/// </summary>
/// <param name="Domain">The domain as configured in <c>AcmeOptions.Domains</c>.</param>
/// <param name="HasCertificate">
///     <see langword="true" /> once the domain has been issued at least one certificate;
///     <see langword="false" /> means none has ever been issued.
/// </param>
/// <param name="IssuedAtUtc">When the current certificate was issued, or <see langword="null" /> if <see cref="HasCertificate" /> is <see langword="false" />.</param>
/// <param name="ExpiresAtUtc">When the current certificate expires, or <see langword="null" /> if <see cref="HasCertificate" /> is <see langword="false" />.</param>
/// <param name="IsDueForRenewal">
///     <see langword="true" /> when the domain has no certificate yet, or its certificate expires
///     within <c>AcmeOptions.RenewBeforeExpiration</c>. It is computed by
///     <c>AcmeCertificateRenewalService.IsDomainDue</c>, the check the renewal loop itself uses.
/// </param>
/// <param name="LastFailureAtUtc">
///     When the domain's most recent issuance or renewal attempt failed, or
///     <see langword="null" /> if that attempt succeeded or none has been made.
/// </param>
/// <param name="LastFailureMessage">
///     A short description of why the most recent attempt failed, or <see langword="null" />
///     together with <see cref="LastFailureAtUtc" />.
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
