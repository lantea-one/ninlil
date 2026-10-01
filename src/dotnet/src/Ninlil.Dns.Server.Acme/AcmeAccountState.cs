// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This record carries the state of the most recent certificate issued for one domain.
/// </summary>
/// <param name="IssuedAtUtc">When the certificate was issued.</param>
/// <param name="ExpiresAtUtc">When the certificate expires, read from the issued certificate.</param>
/// <param name="PfxPath">Where the certificate's PKCS#12 file was written.</param>
public sealed record AcmeDomainCertificateState(
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string PfxPath
);

/// <summary>
///     This record carries the failure of the most recent issuance or renewal attempt for one
///     domain. <c>AcmeCertificateRenewalFailedEvent</c> is only published on the event bus (a
///     webhook can forward it), so this record is what the ACME settings API reads to show the
///     failure. It is kept with <see cref="AcmeDomainCertificateState" /> in the same encrypted
///     <see cref="AcmeAccountState" /> file, and cleared through
///     <see cref="IAcmeAccountStore.ClearDomainFailure" /> when a later attempt for the domain
///     succeeds.
/// </summary>
/// <param name="OccurredAtUtc">When the failure occurred.</param>
/// <param name="ErrorMessage">The message of the exception that ended the attempt.</param>
public sealed record AcmeDomainRenewalFailure(DateTimeOffset OccurredAtUtc, string ErrorMessage);

/// <summary>
///     This record is the complete persisted ACME state that <see cref="FileAcmeAccountStore" />
///     keeps: the account key (generated and registered with the CA once, then reused), the
///     latest certificate state per domain, and the latest failure per domain whose most recent
///     attempt failed.
/// </summary>
/// <param name="AccountKeyPem">
///     The account's private key, PEM-encoded, or <see langword="null" /> until an account has
///     been registered.
/// </param>
/// <param name="Domains">The latest issued certificate state per domain, keyed by domain name.</param>
/// <param name="Failures">
///     The latest failure per domain, keyed by domain name; a domain whose latest attempt
///     succeeded, or that was never attempted, has no entry. It may be <see langword="null" />
///     (for example in a state file without the field) and is then replaced by an empty
///     dictionary.
/// </param>
public sealed record AcmeAccountState(
    string AccountKeyPem,
    Dictionary<string, AcmeDomainCertificateState> Domains,
    Dictionary<string, AcmeDomainRenewalFailure> Failures = null
)
{
    /// <summary>
    ///     This property contains <see cref="Failures" />, never <see langword="null" />: a missing
    ///     value becomes an empty dictionary. <see cref="Domains" /> gets no such fallback; every
    ///     constructor call passes a dictionary.
    /// </summary>
    public Dictionary<string, AcmeDomainRenewalFailure> Failures { get; init; } = Failures ?? [];
}
