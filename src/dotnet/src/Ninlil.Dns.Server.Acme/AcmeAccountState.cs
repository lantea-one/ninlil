// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This class carries a single domain's own most recently issued certificate's state.
/// </summary>
/// <param name="IssuedAtUtc">When this certificate was issued.</param>
/// <param name="ExpiresAtUtc">When this certificate actually expires, read from the real, issued certificate itself.</param>
/// <param name="PfxPath">Where this certificate's own PKCS#12 file was written.</param>
public sealed record AcmeDomainCertificateState(
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string PfxPath
);

/// <summary>
///     This class carries a single domain's own most recent issuance/renewal attempt's failure --
///     durable, unlike <c>AcmeCertificateRenewalFailedEvent</c>, which is only ever published to
///     the in-process event bus and has no subscriber persisting it anywhere a later REST read
///     could find it (confirmed by checking <c>Server.Webhooks</c>'/<c>Server.QueryLog</c>'s own
///     subscriber registrations -- neither one is wired to this event type). Kept alongside
///     <see cref="AcmeDomainCertificateState" /> in the same <see cref="AcmeAccountState" /> file
///     (and the same encrypted-at-rest store, via <see cref="IAcmeAccountStore" />) rather than as
///     a new, separate persistence mechanism, since it is exactly the same shape of fact --
///     "this domain's own most recent attempt outcome" -- just for the failure branch instead of
///     the success branch. A domain's own entry here is cleared (via
///     <see cref="IAcmeAccountStore.ClearDomainFailure" />) the moment a later attempt for the
///     same domain succeeds, so a resolved domain does not show a stale failure forever.
/// </summary>
/// <param name="OccurredAtUtc">When this failure occurred.</param>
/// <param name="ErrorMessage">A short, human-readable description of why the attempt failed.</param>
public sealed record AcmeDomainRenewalFailure(DateTimeOffset OccurredAtUtc, string ErrorMessage);

/// <summary>
///     This class carries this resolver's own complete, persisted ACME state: its long-term
///     account key (generated and registered with the CA once, then reused for the lifetime of
///     this installation), every domain's own most recently issued certificate state, and every
///     domain's own most recent renewal *failure*, if its most recent attempt did not succeed.
/// </summary>
/// <param name="AccountKeyPem">
///     The account's own private key, PEM-encoded, or <see langword="null" /> until an account
///     has actually been registered.
/// </param>
/// <param name="Domains">Every domain's own most recently issued certificate state, keyed by domain name.</param>
/// <param name="Failures">
///     Every domain's own most recent renewal failure, keyed by domain name -- absent for a
///     domain whose most recent attempt succeeded (or that has never been attempted at all).
///     Defaults to an empty dictionary so a state file persisted before this property existed
///     still deserializes cleanly.
/// </param>
public sealed record AcmeAccountState(
    string AccountKeyPem,
    Dictionary<string, AcmeDomainCertificateState> Domains,
    Dictionary<string, AcmeDomainRenewalFailure> Failures = null
)
{
    /// <summary>
    ///     This property contains <see cref="Failures" />, never <see langword="null" /> --
    ///     normalizing a state file written before this property existed (or a hand-constructed
    ///     instance that left the constructor parameter at its default) to a real, empty
    ///     dictionary, the same defensive normalization <see cref="Domains" /> already gets
    ///     implicitly by never being constructed as <see langword="null" /> anywhere in this
    ///     project.
    /// </summary>
    public Dictionary<string, AcmeDomainRenewalFailure> Failures { get; init; } = Failures ?? [];
}
