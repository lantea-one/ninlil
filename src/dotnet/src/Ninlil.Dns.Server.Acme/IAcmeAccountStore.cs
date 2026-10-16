// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This interface stores the ACME account key and the per-domain certificate and failure
///     state across restarts. <see cref="AcmeCertificateIssuer" /> and
///     <see cref="AcmeCertificateRenewalService" /> write it, and the ACME settings API reads it.
///     <see cref="FileAcmeAccountStore" /> implements it. Every member that reads the state may
///     throw <see cref="InvalidOperationException" /> while the state file cannot be read; the
///     next call tries again.
/// </summary>
public interface IAcmeAccountStore
{
    /// <summary>
    ///     This property contains the account's private key, PEM-encoded, or
    ///     <see langword="null" /> until <see cref="SaveAccountKey" /> has been called for the
    ///     first time.
    /// </summary>
    string AccountKeyPem { get; }

    /// <summary>
    ///     This property contains the latest issued certificate state per domain.
    /// </summary>
    IReadOnlyDictionary<string, AcmeDomainCertificateState> DomainStates { get; }

    /// <summary>
    ///     This property contains the latest failure per domain; a domain whose latest attempt
    ///     succeeded, or that was never attempted, has no entry (see
    ///     <see cref="AcmeDomainRenewalFailure" />).
    /// </summary>
    IReadOnlyDictionary<string, AcmeDomainRenewalFailure> Failures { get; }

    /// <summary>
    ///     This method stores a newly registered account's private key.
    /// </summary>
    void SaveAccountKey(string accountKeyPem);

    /// <summary>
    ///     This method stores a domain's newly issued certificate state.
    /// </summary>
    void SaveDomainState(string domain, AcmeDomainCertificateState state);

    /// <summary>
    ///     This method stores a domain's latest issuance or renewal failure, replacing any earlier
    ///     one.
    /// </summary>
    void SaveDomainFailure(string domain, AcmeDomainRenewalFailure failure);

    /// <summary>
    ///     This method clears a domain's recorded failure. It is called when a later attempt
    ///     succeeds, so a recovered domain stops showing the old failure, and does nothing when
    ///     no failure is recorded.
    /// </summary>
    void ClearDomainFailure(string domain);
}
