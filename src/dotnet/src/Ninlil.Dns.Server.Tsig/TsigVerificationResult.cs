using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig;

/// <summary>
///     This class carries the outcome of an <see cref="ITsigService" /> verification: either the
///     signature was valid, or it was rejected with an RFC 8945 §5.2 error (BADKEY, BADSIG,
///     BADTIME) that the caller can report back to the sender.
/// </summary>
public sealed class TsigVerificationResult
{
    /// <summary>
    ///     This property indicates whether the message's signature was valid.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    ///     This property contains the reason verification failed, or
    ///     <see cref="DnsResponseCodeEnum.NoError" /> when <see cref="IsValid" /> is
    ///     <see langword="true" />.
    /// </summary>
    public DnsResponseCodeEnum Error { get; }

    /// <summary>
    ///     This property contains the key named in the message's TSIG record, when that key exists,
    ///     is enabled and the check got far enough to look it up. It is set on a BADTIME or BADSIG
    ///     failure and <see langword="null" /> on a BADKEY failure.
    /// </summary>
    public TsigKeyEntity Key { get; }

    /// <summary>
    ///     This constructor is private; use <see cref="Success" /> or <see cref="Failure" />.
    /// </summary>
    private TsigVerificationResult(bool isValid, DnsResponseCodeEnum error, TsigKeyEntity key)
    {
        IsValid = isValid;
        Error = error;
        Key = key;
    }

    /// <summary>
    ///     This method builds the result for a message whose signature verified successfully.
    /// </summary>
    /// <param name="key">The key the message was verified against.</param>
    public static TsigVerificationResult Success(TsigKeyEntity key) =>
        new(true, DnsResponseCodeEnum.NoError, key);

    /// <summary>
    ///     This method builds the result for a message that was rejected.
    /// </summary>
    /// <param name="error">Why the message was rejected.</param>
    /// <param name="key">
    ///     The key the message claimed to be signed with, if one was found by that name, or
    ///     <see langword="null" /> otherwise.
    /// </param>
    public static TsigVerificationResult Failure(
        DnsResponseCodeEnum error,
        TsigKeyEntity key = null
    ) => new(false, error, key);
}
