// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This record carries the outcome of one validation by <see cref="IDnssecValidator" />.
/// </summary>
/// <param name="State">Which of RFC 4035 §4.3's four states this attempt concluded in.</param>
/// <param name="Reason">
///     A short explanation of why <see cref="State" /> is
///     <see cref="DnssecValidationStateEnum.Bogus" /> or <see cref="DnssecValidationStateEnum.Indeterminate" />,
///     for logs and tests; clients never see it. <see langword="null" /> for
///     <see cref="DnssecValidationStateEnum.Secure" />/<see cref="DnssecValidationStateEnum.Insecure" />.
/// </param>
public sealed record DnssecValidationResult(DnssecValidationStateEnum State, string Reason = null)
{
    /// <summary>
    ///     This property contains how many public-key signature checks the validation spent, for
    ///     logs and tests (a validation that finds every zone in the trust cache spends one per
    ///     RRset). It is zero on the factory results and is set by <see cref="DnssecValidator" />.
    /// </summary>
    public int SignatureChecksUsed { get; init; }

    /// <summary>
    ///     This method builds a <see cref="DnssecValidationStateEnum.Secure" /> result.
    /// </summary>
    public static DnssecValidationResult Secure() => new(DnssecValidationStateEnum.Secure);

    /// <summary>
    ///     This method builds a <see cref="DnssecValidationStateEnum.Insecure" /> result.
    /// </summary>
    public static DnssecValidationResult Insecure() => new(DnssecValidationStateEnum.Insecure);

    /// <summary>
    ///     This method builds a <see cref="DnssecValidationStateEnum.Bogus" /> result.
    /// </summary>
    public static DnssecValidationResult Bogus(string reason) =>
        new(DnssecValidationStateEnum.Bogus, reason);

    /// <summary>
    ///     This method builds a <see cref="DnssecValidationStateEnum.Indeterminate" /> result.
    /// </summary>
    public static DnssecValidationResult Indeterminate(string reason) =>
        new(DnssecValidationStateEnum.Indeterminate, reason);
}
