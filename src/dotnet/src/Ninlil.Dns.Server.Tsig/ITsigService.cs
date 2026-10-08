using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig;

/// <summary>
///     This interface is the TSIG (RFC 8945) engine. It signs outgoing messages with a
///     pre-shared key and verifies incoming ones against the key they name. The query pipeline
///     verifies signed QUERY, NOTIFY and UPDATE requests and signs their replies through
///     <see cref="TsigTransactionSignatureService" />. Zone transfers (<c>ZoneTransferService</c>)
///     refuse a request without a valid TSIG signature; the zone transfer clients use it to sign
///     their requests and verify the responses. Keys come from
///     <see cref="Ninlil.Dns.Data.Abstractions.Repository.ITsigKeyRepository" />.
/// </summary>
public interface ITsigService
{
    /// <summary>
    ///     This method verifies a signed message, in the order of RFC 8945 §5.2: algorithm and key
    ///     (unknown, disabled, cluster or of another algorithm: BADKEY), MAC length (FORMERR or
    ///     BADTRUNC), MAC (BADSIG), then time (BADTIME). Key lookups are cached for a few seconds
    ///     (<see cref="TsigKeyLookupCache" />).
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
    ///     Thrown when no key named <paramref name="keyName" /> exists, or it is disabled, or it
    ///     is a cluster key (a peer's key or an invite key), which only the cluster channel may use.
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
    ///     The request MAC is digested with its two-octet MAC Size in front, as RFC 8945 §5.3
    ///     requires, so other implementations (BIND, dnspython) verify the answer.
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
    ///     Thrown when no key named <paramref name="keyName" /> exists, or it is disabled, or it
    ///     is a cluster key (a peer's key or an invite key), which only the cluster channel may use.
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
    ///     Timers (Time Signed and Fudge), not the full TSIG Variables. The prior MAC carries its
    ///     two-octet MAC Size. The outbound zone transfer
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
    ///     Thrown when no key named <paramref name="keyName" /> exists, or it is disabled, or it
    ///     is a cluster key (a peer's key or an invite key), which only the cluster channel may use.
    /// </exception>
    Task<DnsMessage> SignContinuationAsync(
        DnsMessage message,
        string keyName,
        ushort originalId,
        byte[] priorMac,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method turns <paramref name="response" /> into the reply to a request whose TSIG
    ///     failed verification (RFC 8945 §5.3.2). The result depends on the failure:
    ///     <list type="bullet">
    ///         <item>
    ///             FORMERR: the RCODE is FORMERR and the reply has no TSIG record.
    ///         </item>
    ///         <item>
    ///             BADKEY, BADSIG, BADTRUNC: the RCODE is NOTAUTH and the TSIG record carries the
    ///             request's key and algorithm names, the current time, an empty MAC (size 0), the
    ///             response ID as Original ID and the error code. The reply is never signed.
    ///         </item>
    ///         <item>
    ///             BADTIME (RFC 8945 §5.2.3): the RCODE is NOTAUTH and the reply is signed as a
    ///             first response over the request MAC. Time Signed is the request's, Other Data is
    ///             the server's current time. The sender proved it holds the key, so it may learn
    ///             the server time.
    ///         </item>
    ///     </list>
    ///     The extended codes never go into the header RCODE, which has four bits.
    /// </summary>
    /// <param name="response">The reply to fill in; its ID and question are already set.</param>
    /// <param name="requestTsig">The TSIG record of the failed request.</param>
    /// <param name="failure">The failed verification result.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns><paramref name="response" />, with its RCODE and TSIG set as above.</returns>
    Task<DnsMessage> SignErrorResponseAsync(
        DnsMessage response,
        DnsTsigOptions requestTsig,
        TsigVerificationResult failure,
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
    /// <param name="expectedKeyName">
    ///     The name of the key that signed the request. A reply signed with any other key fails
    ///     with BADKEY, even when that key is enabled and the MAC is right for it.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The verification outcome.</returns>
    Task<TsigVerificationResult> VerifyResponseAsync(
        byte[] rawMessage,
        int tsigRecordStartOffset,
        DnsMessage message,
        byte[] requestMac,
        string expectedKeyName,
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
    /// <param name="expectedKeyName">
    ///     The name of the key that signed the request. A message signed with any other key fails
    ///     with BADKEY.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The verification outcome.</returns>
    Task<TsigVerificationResult> VerifyContinuationAsync(
        byte[] rawMessage,
        int tsigRecordStartOffset,
        DnsMessage message,
        byte[] priorMac,
        IReadOnlyList<byte[]> unsignedMessageBytesSincePriorTsig,
        string expectedKeyName,
        CancellationToken stoppingToken = default
    );
}
