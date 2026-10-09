using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Caching;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This record is what the validator knows about one zone's place in the chain of trust:
///     whether the zone is secure, provably unsigned or unusable, and for a secure zone its
///     validated DNSKEY RRset. <see cref="DnssecValidator" /> computes it once per zone and
///     validation, and <see cref="DnssecTrustCache" /> keeps secure and insecure results until
///     <see cref="ExpiresAtUtc" />.
/// </summary>
/// <param name="State">
///     <see cref="DnssecValidationStateEnum.Secure" /> (keys are set),
///     <see cref="DnssecValidationStateEnum.Insecure" /> (a signed proof, or an insecure ancestor,
///     shows the zone is unsigned), <see cref="DnssecValidationStateEnum.Bogus" /> (the chain
///     failed) or <see cref="DnssecValidationStateEnum.Indeterminate" /> (no trust anchor).
/// </param>
/// <param name="Keys">The zone's validated DNSKEY RRset; <see langword="null" /> unless secure.</param>
/// <param name="ExpiresAtUtc">
///     When the result stops being valid: the earliest of the TTLs and signature expirations of the
///     records that proved it, and the parent's expiry.
/// </param>
/// <param name="Reason">Why the state is Bogus or Indeterminate; <see langword="null" /> otherwise.</param>
public sealed record ZoneTrust(
    DnssecValidationStateEnum State,
    IReadOnlyList<DnsResourceRecord> Keys,
    DateTimeOffset ExpiresAtUtc,
    string Reason
) : IDnsCacheEntry
{
    /// <summary>
    ///     This method builds a secure result.
    /// </summary>
    public static ZoneTrust Secure(IReadOnlyList<DnsResourceRecord> keys, DateTimeOffset expires) =>
        new(DnssecValidationStateEnum.Secure, keys, expires, null);

    /// <summary>
    ///     This method builds an insecure result.
    /// </summary>
    public static ZoneTrust Insecure(DateTimeOffset expires) =>
        new(DnssecValidationStateEnum.Insecure, null, expires, null);

    /// <summary>
    ///     This method builds a failed result. It is never cached.
    /// </summary>
    public static ZoneTrust Bogus(string reason) =>
        new(DnssecValidationStateEnum.Bogus, null, DateTimeOffset.MinValue, reason);

    /// <summary>
    ///     This method builds the result for a validation without a usable trust anchor. It is
    ///     never cached.
    /// </summary>
    public static ZoneTrust Indeterminate(string reason) =>
        new(DnssecValidationStateEnum.Indeterminate, null, DateTimeOffset.MinValue, reason);
}
