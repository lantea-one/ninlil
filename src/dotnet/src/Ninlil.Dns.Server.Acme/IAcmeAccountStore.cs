// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This interface holds this resolver's own ACME account key and every domain's own
///     certificate state durably, across restarts.
/// </summary>
public interface IAcmeAccountStore
{
    /// <summary>
    ///     This property contains the account's own private key, PEM-encoded, or
    ///     <see langword="null" /> until <see cref="SaveAccountKey" /> has been called for the
    ///     first time.
    /// </summary>
    string AccountKeyPem { get; }

    /// <summary>
    ///     This property contains every domain's own most recently issued certificate state.
    /// </summary>
    IReadOnlyDictionary<string, AcmeDomainCertificateState> DomainStates { get; }

    /// <summary>
    ///     This property contains every domain's own most recent renewal failure -- absent for a
    ///     domain whose most recent attempt succeeded, or that was never attempted at all. See
    ///     <see cref="AcmeDomainRenewalFailure" />'s own doc comment for why this durable record
    ///     exists at all, alongside the event-bus-only <c>AcmeCertificateRenewalFailedEvent</c>.
    /// </summary>
    IReadOnlyDictionary<string, AcmeDomainRenewalFailure> Failures { get; }

    /// <summary>
    ///     This method persists a newly registered account's own private key.
    /// </summary>
    void SaveAccountKey(string accountKeyPem);

    /// <summary>
    ///     This method persists a single domain's own newly issued certificate state.
    /// </summary>
    void SaveDomainState(string domain, AcmeDomainCertificateState state);

    /// <summary>
    ///     This method persists a single domain's own most recent issuance/renewal failure,
    ///     overwriting whatever failure (if any) was previously recorded for it.
    /// </summary>
    void SaveDomainFailure(string domain, AcmeDomainRenewalFailure failure);

    /// <summary>
    ///     This method clears a single domain's own most recently recorded failure -- called the
    ///     moment a later attempt for the same domain succeeds, so a domain that has recovered
    ///     does not keep showing a stale failure forever. A no-op when no failure is currently
    ///     recorded for <paramref name="domain" />.
    /// </summary>
    void ClearDomainFailure(string domain);
}
