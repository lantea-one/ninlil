using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig;

/// <summary>
///     This interface is Ninlil's TSIG (RFC 8945) engine: signing outgoing messages with a
///     pre-shared key, and verifying incoming ones against whichever key they claim to be signed
///     with. Zone transfers and dynamic update both depend on this for authorization -- both
///     require a valid TSIG signature before the underlying operation is permitted.
/// </summary>
public interface ITsigService
{
    /// <summary>
    ///     This method verifies a signed message.
    /// </summary>
    /// <param name="rawMessage">The complete message, exactly as received from the wire.</param>
    /// <param name="tsigRecordStartOffset">
    ///     The offset within <paramref name="rawMessage" /> the TSIG record started at, as
    ///     reported by <see cref="DnsMessageReader" />'s message-decoding overload that reports
    ///     the TSIG record's starting offset.
    /// </param>
    /// <param name="message"><paramref name="rawMessage" />, already decoded.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The verification outcome.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="message" /> carries no TSIG record at all -- whether that
    ///     is itself an error (a signature was required but absent) is a policy decision for the
    ///     caller to make before ever calling this method, not something this service assumes.
    /// </exception>
    Task<TsigVerificationResult> VerifyAsync(
        byte[] rawMessage,
        int tsigRecordStartOffset,
        DnsMessage message,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method signs an outgoing message with a named, pre-shared key, setting its
    ///     <see cref="DnsMessage.Tsig" /> property to the computed result.
    /// </summary>
    /// <param name="message">
    ///     The message to sign. Must not already carry a TSIG record -- signing computes the MAC
    ///     over the message exactly as it stands, so nothing about it may change afterward
    ///     except attaching the resulting <see cref="DnsTsigOptions" />.
    /// </param>
    /// <param name="keyName">The name of the pre-shared key to sign with.</param>
    /// <param name="originalId">
    ///     The message's own header ID, recorded in the TSIG record so a verifier can detect if
    ///     an intermediary rewrote it afterward.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns><paramref name="message" />, with <see cref="DnsMessage.Tsig" /> now set.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when no key named <paramref name="keyName" /> exists, or it is disabled.
    /// </exception>
    Task<DnsMessage> SignAsync(
        DnsMessage message,
        string keyName,
        ushort originalId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method signs the first message of a multi-message answer sequence -- or an
    ///     ordinary, single-message answer, which RFC 8945 §5.3.1 treats identically -- per RFC
    ///     8945 §5.3's own "Generation of TSIG on Answers" digest formula: the request's own MAC,
    ///     prefixed ahead of the response's message bytes and full TSIG Variables, so the response
    ///     is cryptographically bound to the specific request that produced it rather than any
    ///     other request the same key could have signed.
    /// </summary>
    /// <param name="message">The response message to sign, not yet carrying a TSIG record.</param>
    /// <param name="keyName">The name of the pre-shared key to sign with.</param>
    /// <param name="originalId">The originating request's own header ID.</param>
    /// <param name="requestMac">
    ///     The MAC from the request this message answers, as verified via <see cref="VerifyAsync" />
    ///     or <see cref="VerifyResponseAsync" />.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns><paramref name="message" />, with <see cref="DnsMessage.Tsig" /> now set.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when no key named <paramref name="keyName" /> exists, or it is disabled.
    /// </exception>
    Task<DnsMessage> SignResponseAsync(
        DnsMessage message,
        string keyName,
        ushort originalId,
        byte[] requestMac,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method signs the second or a later message within a multi-message answer sequence,
    ///     per RFC 8945 §5.3.1's own reduced digest formula for such a message: the running prior
    ///     MAC (from whichever earlier message in the sequence most recently carried one),
    ///     prefixed ahead of this message's own bytes and its own "TSIG Timers" (Time Signed and
    ///     Fudge only -- <em>not</em> the full TSIG Variables <see cref="SignResponseAsync" /> uses
    ///     for the sequence's first message). This project's own outbound zone-transfer path
    ///     always signs every message in a sequence this way (never omitting one, the more
    ///     thorough of the two RFC-permitted choices), so this method never needs to account for
    ///     an unsigned message appearing in between -- see
    ///     <see cref="VerifyContinuationAsync" />'s own doc comment for why that same tolerance is
    ///     still needed on the verifying side, for a real-world peer that chooses the lighter
    ///     option instead.
    /// </summary>
    /// <param name="message">The response message to sign, not yet carrying a TSIG record.</param>
    /// <param name="keyName">The name of the pre-shared key to sign with.</param>
    /// <param name="originalId">The originating request's own header ID.</param>
    /// <param name="priorMac">
    ///     The MAC from the TSIG record most recently attached within this same sequence --
    ///     directly the previous message's own MAC when every message is signed, as this
    ///     project's own outbound path always does.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns><paramref name="message" />, with <see cref="DnsMessage.Tsig" /> now set.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when no key named <paramref name="keyName" /> exists, or it is disabled.
    /// </exception>
    Task<DnsMessage> SignContinuationAsync(
        DnsMessage message,
        string keyName,
        ushort originalId,
        byte[] priorMac,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method verifies the first message of a multi-message answer sequence -- or an
    ///     ordinary, single-message answer -- per RFC 8945 §5.3's digest formula, the mirror image
    ///     of <see cref="SignResponseAsync" />.
    /// </summary>
    /// <param name="rawMessage">The complete message, exactly as received from the wire.</param>
    /// <param name="tsigRecordStartOffset">
    ///     The offset within <paramref name="rawMessage" /> the TSIG record started at, as
    ///     reported by <see cref="DnsMessageReader" />'s message-decoding overload that reports
    ///     the TSIG record's starting offset.
    /// </param>
    /// <param name="message"><paramref name="rawMessage" />, already decoded.</param>
    /// <param name="requestMac">The MAC from the request this message claims to answer.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The verification outcome.</returns>
    Task<TsigVerificationResult> VerifyResponseAsync(
        byte[] rawMessage,
        int tsigRecordStartOffset,
        DnsMessage message,
        byte[] requestMac,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method verifies the second or a later message within a multi-message answer
    ///     sequence, per RFC 8945 §5.3.1's reduced digest formula, the mirror image of
    ///     <see cref="SignContinuationAsync" />. Unlike the signing side, this method also accepts
    ///     a real-world peer's own choice to leave some messages in the sequence entirely unsigned
    ///     (RFC 8945 §5.4 requires a verifier to tolerate up to 99 consecutive unsigned messages,
    ///     as long as the sequence's own first and last message both carry a valid TSIG) --
    ///     <paramref name="unsignedMessageBytesSincePriorTsig" /> carries exactly those messages'
    ///     own raw bytes, in order, so this method can fold them into the digest exactly where RFC
    ///     8945 §5.3.1's own "DNS Messages" component requires: between the running prior MAC and
    ///     this message's own bytes.
    /// </summary>
    /// <param name="rawMessage">The complete message, exactly as received from the wire.</param>
    /// <param name="tsigRecordStartOffset">
    ///     The offset within <paramref name="rawMessage" /> the TSIG record started at, as
    ///     reported by <see cref="DnsMessageReader" />'s message-decoding overload that reports
    ///     the TSIG record's starting offset.
    /// </param>
    /// <param name="message"><paramref name="rawMessage" />, already decoded.</param>
    /// <param name="priorMac">
    ///     The MAC from the TSIG record most recently attached within this same sequence.
    /// </param>
    /// <param name="unsignedMessageBytesSincePriorTsig">
    ///     The complete raw bytes of every message received since (and not including) whichever
    ///     earlier message <paramref name="priorMac" /> came from, that itself carried no TSIG
    ///     record at all -- empty when this message is the very next one after the last
    ///     TSIG-bearing message, as is always true against this project's own outbound path,
    ///     which never leaves a message unsigned.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The verification outcome.</returns>
    Task<TsigVerificationResult> VerifyContinuationAsync(
        byte[] rawMessage,
        int tsigRecordStartOffset,
        DnsMessage message,
        byte[] priorMac,
        IReadOnlyList<byte[]> unsignedMessageBytesSincePriorTsig,
        CancellationToken stoppingToken = default
    );
}
