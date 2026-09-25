using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig;

/// <summary>
///     This interface is the TSIG (RFC 8945) engine. It signs outgoing messages with a
///     pre-shared key and verifies incoming ones against the key they name. Zone transfers
///     (<c>ZoneTransferService</c>) and dynamic updates (<c>DynamicUpdateQueryResolver</c>) both
///     refuse a request without a valid TSIG signature; the zone transfer clients use it to sign
///     their requests and verify the responses. Keys come from
///     <see cref="Ninlil.Dns.Data.Abstractions.Repository.ITsigKeyRepository" />.
/// </summary>
public interface ITsigService
{
    /// <summary>
    ///     This method verifies a signed message.
    /// </summary>
    /// <param name="rawMessage">The complete message as received from the wire.</param>
    /// <param name="tsigRecordStartOffset">
    ///     The offset within <paramref name="rawMessage" /> where the TSIG record starts, as
    ///     reported by <see cref="DnsMessageReader" /> when it decodes the message.
    /// </param>
    /// <param name="message"><paramref name="rawMessage" />, already decoded.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The verification outcome.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="message" /> has no TSIG record. Whether a missing
    ///     signature is an error is the caller's decision, made before calling this method.
    /// </exception>
    Task<TsigVerificationResult> VerifyAsync(
        byte[] rawMessage,
        int tsigRecordStartOffset,
        DnsMessage message,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method signs an outgoing request with a named pre-shared key and sets its
    ///     <see cref="DnsMessage.Tsig" /> property to the result.
    /// </summary>
    /// <param name="message">
    ///     The message to sign. It must not already carry a TSIG record. The MAC covers the
    ///     message as it stands, so nothing may change afterward except attaching the
    ///     <see cref="DnsTsigOptions" />.
    /// </param>
    /// <param name="keyName">The name of the pre-shared key to sign with.</param>
    /// <param name="originalId">
    ///     The message's header ID, recorded in the TSIG record (Original ID) so the ID can be
    ///     restored if an intermediary rewrites it.
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
    ///     This method signs a single-message answer, or the first message of a multi-message
    ///     answer, per RFC 8945 §5.3. The digest is the request's MAC, then the response bytes,
    ///     then the full TSIG Variables. Including the request MAC binds the response to the
    ///     request that produced it.
    /// </summary>
    /// <remarks>
    ///     The request MAC is digested as raw MAC bytes, without the two-byte MAC Size field.
    ///     <see cref="VerifyResponseAsync" /> does the same, so both ends of a Ninlil-to-Ninlil
    ///     transfer agree.
    /// </remarks>
    /// <param name="message">The response message to sign, not yet carrying a TSIG record.</param>
    /// <param name="keyName">The name of the pre-shared key to sign with.</param>
    /// <param name="originalId">The originating request's header ID.</param>
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
    ///     This method signs the second or a later message of a multi-message answer, per RFC 8945
    ///     §5.3.1. The digest is the prior MAC, then this message's bytes, then only the TSIG
    ///     Timers (Time Signed and Fudge), not the full TSIG Variables. The outbound zone transfer
    ///     path signs every message, so no unsigned messages ever sit between two signed ones
    ///     here; <see cref="VerifyContinuationAsync" /> still has to allow for them from peers.
    /// </summary>
    /// <param name="message">The response message to sign, not yet carrying a TSIG record.</param>
    /// <param name="keyName">The name of the pre-shared key to sign with.</param>
    /// <param name="originalId">The originating request's header ID.</param>
    /// <param name="priorMac">
    ///     The MAC of the previous message in the sequence.
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
    ///     This method verifies a single-message answer, or the first message of a multi-message
    ///     answer, per RFC 8945 §5.3. It is the counterpart of <see cref="SignResponseAsync" />.
    /// </summary>
    /// <param name="rawMessage">The complete message as received from the wire.</param>
    /// <param name="tsigRecordStartOffset">
    ///     The offset within <paramref name="rawMessage" /> where the TSIG record starts, as
    ///     reported by <see cref="DnsMessageReader" /> when it decodes the message.
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
    ///     This method verifies the second or a later message of a multi-message answer, per RFC
    ///     8945 §5.3.1. It is the counterpart of <see cref="SignContinuationAsync" />, but also
    ///     accepts a peer that leaves some messages unsigned (RFC 8945 §5.3.1 allows up to 99 in
    ///     a row, as long as the first and last messages are signed). The bytes of those unsigned
    ///     messages go into the digest between the prior MAC and this message's bytes.
    /// </summary>
    /// <param name="rawMessage">The complete message as received from the wire.</param>
    /// <param name="tsigRecordStartOffset">
    ///     The offset within <paramref name="rawMessage" /> where the TSIG record starts, as
    ///     reported by <see cref="DnsMessageReader" /> when it decodes the message.
    /// </param>
    /// <param name="message"><paramref name="rawMessage" />, already decoded.</param>
    /// <param name="priorMac">
    ///     The MAC of the most recent signed message in the sequence.
    /// </param>
    /// <param name="unsignedMessageBytesSincePriorTsig">
    ///     The raw bytes of each unsigned message received after the one
    ///     <paramref name="priorMac" /> came from, in order. Empty when the previous message was
    ///     signed.
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
