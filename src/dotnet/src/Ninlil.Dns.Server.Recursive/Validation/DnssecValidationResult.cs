// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This class carries a single validation attempt's outcome.
/// </summary>
/// <param name="State">Which of RFC 4035 §4.3's four states this attempt concluded in.</param>
/// <param name="Reason">
///     A short, human-readable explanation of why <see cref="State" /> is
///     <see cref="DnssecValidationStateEnum.Bogus" /> or <see cref="DnssecValidationStateEnum.Indeterminate" />
///     -- for logging, since a client is never shown this directly. <see langword="null" /> for
///     <see cref="DnssecValidationStateEnum.Secure" />/<see cref="DnssecValidationStateEnum.Insecure" />.
/// </param>
public sealed record DnssecValidationResult(DnssecValidationStateEnum State, string Reason = null)
{
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
