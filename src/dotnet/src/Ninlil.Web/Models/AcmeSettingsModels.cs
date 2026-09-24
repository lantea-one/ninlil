// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>AcmeOptions</c> plus the global-default <c>CaaAutoManagementOptions</c> (nested under
///     <see cref="CaaAutoManagement" />, mirroring how the per-zone override already lives
///     alongside a zone's own other fields on <c>ZonesController</c>). No secret material is
///     involved -- the account's own private key is never exposed anywhere in this REST API, the
///     same "never expose this over REST, at any point" posture <c>DnssecKeysController</c>
///     already applies to a DNSSEC private key -- so this record's own fields are the exact same
///     ones a <c>PATCH</c> to the same endpoint accepts.
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
///     This record is the REST representation of <c>CaaAutoManagementOptions</c>'s own global
///     defaults, nested within <see cref="AcmeSettingsDetail.CaaAutoManagement" />.
/// </summary>
public record CaaAutoManagementSettingsDetail(bool DefaultEnabled, string DefaultIssuerDomain);

/// <summary>
///     This record is one row of the real, per-domain certificate inventory
///     <c>AcmeSettingsController.Certificates</c> returns -- every domain
///     <c>AcmeOptions.Domains</c> currently names, alongside whatever <c>IAcmeAccountStore</c>
///     actually knows about it. A domain that has never had a certificate successfully issued is
///     always visibly distinguishable from one that simply is not due for renewal yet:
///     <see cref="HasCertificate" /> is <see langword="false" /> for the former and
///     <see langword="true" /> for the latter, regardless of what <see cref="IsDueForRenewal" />
///     itself reports for either one.
/// </summary>
/// <param name="Domain">The domain, exactly as configured in <c>AcmeOptions.Domains</c>.</param>
/// <param name="HasCertificate">
///     <see langword="true" /> once this domain has had at least one certificate successfully
///     issued -- <see langword="false" /> means no certificate has ever been issued for it at
///     all, a real, distinct state from "issued, but not due for renewal yet."
/// </param>
/// <param name="IssuedAtUtc">When the current certificate was issued, or <see langword="null" /> if <see cref="HasCertificate" /> is <see langword="false" />.</param>
/// <param name="ExpiresAtUtc">When the current certificate expires, or <see langword="null" /> if <see cref="HasCertificate" /> is <see langword="false" />.</param>
/// <param name="IsDueForRenewal">
///     <see langword="true" /> when this domain has no certificate yet, or its current one is
///     inside <c>AcmeOptions.RenewBeforeExpiration</c> of its own expiry -- the exact same
///     condition <c>AcmeCertificateRenewalService</c>'s own poll loop uses to decide whether to
///     actually attempt an issuance/renewal on its next tick, computed by the same shared method
///     so this can never drift from what the background loop itself will do.
/// </param>
/// <param name="LastFailureAtUtc">
///     When this domain's own most recent issuance/renewal attempt failed, or
///     <see langword="null" /> if its most recent attempt succeeded (or it has never been
///     attempted at all).
/// </param>
/// <param name="LastFailureMessage">
///     A short, human-readable description of why the most recent attempt failed, or
///     <see langword="null" /> alongside <see cref="LastFailureAtUtc" />.
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
