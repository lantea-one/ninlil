// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This class bounds the public-key signature checks of one validation. A response can carry
///     many RRSIGs and many DNSKEYs with the same key tag, so trying every pair costs a quadratic
///     number of public-key operations (the "KeyTrap" attack class). <see cref="DnssecValidator" />
///     creates one budget per validation and passes it to every
///     <see cref="RrsetSignatureVerifier.Verify" /> call; each call starts a new RRset with
///     <see cref="BeginRrset" /> and spends one unit per public-key check with
///     <see cref="TryConsume" />. The class is not thread-safe: one validation uses it from one
///     logical flow.
/// </summary>
/// <param name="maxPerRrset">The most signature checks for one RRset.</param>
/// <param name="maxPerValidation">The most signature checks for the whole validation.</param>
public sealed class SignatureCheckBudget(
    int maxPerRrset = SignatureCheckBudget.DefaultMaxPerRrset,
    int maxPerValidation = SignatureCheckBudget.DefaultMaxPerValidation
)
{
    /// <summary>
    ///     This constant contains the default limit per RRset. A signed RRset normally has one or
    ///     two RRSIGs, so 8 leaves room for an algorithm rollover.
    /// </summary>
    public const int DefaultMaxPerRrset = 8;

    /// <summary>
    ///     This constant contains the default limit for one validation: a chain of a few zones, each
    ///     with a DS, a DNSKEY and an answer RRset.
    /// </summary>
    public const int DefaultMaxPerValidation = 64;

    private int usedForRrset;

    /// <summary>
    ///     This property contains the number of signature checks spent so far in this validation.
    /// </summary>
    public int TotalUsed { get; private set; }

    /// <summary>
    ///     This method starts counting for a new RRset.
    /// </summary>
    public void BeginRrset() => usedForRrset = 0;

    /// <summary>
    ///     This method spends one signature check.
    /// </summary>
    /// <returns>
    ///     <see langword="false" /> when the RRset's or the validation's limit is used up, in which
    ///     case the caller must not do the check.
    /// </returns>
    public bool TryConsume()
    {
        if (usedForRrset >= maxPerRrset || TotalUsed >= maxPerValidation)
            return false;

        usedForRrset++;
        TotalUsed++;
        return true;
    }
}
