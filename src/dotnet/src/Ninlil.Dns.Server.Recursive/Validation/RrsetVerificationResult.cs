using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This record is the outcome of <see cref="RrsetSignatureVerifier.Verify" />: whether an RRset
///     verified and, if so, with which RRSIG, or why it did not.
/// </summary>
/// <param name="IsVerified">Whether at least one RRSIG verified against a trusted key.</param>
/// <param name="VerifyingSignature">
///     The RRSIG that verified, or <see langword="null" /> when <paramref name="IsVerified" /> is
///     <see langword="false" />. B8-6 reads its Labels field to detect wildcard expansion.
/// </param>
/// <param name="FailureReason">
///     A short English reason for the failure, or <see langword="null" /> when the RRset verified.
///     The validator puts it in the Bogus result's reason.
/// </param>
public sealed record RrsetVerificationResult(
    bool IsVerified,
    RrSigRecordData VerifyingSignature,
    string FailureReason
)
{
    /// <summary>
    ///     This method builds the result of a successful verification.
    /// </summary>
    public static RrsetVerificationResult Success(RrSigRecordData signature) =>
        new(true, signature, null);

    /// <summary>
    ///     This method builds the result of a failed verification.
    /// </summary>
    public static RrsetVerificationResult Failure(string reason) => new(false, null, reason);
}
