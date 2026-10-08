using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Tsig.Options;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig;

/// <summary>
///     This class implements <see cref="ITsigService" /> (RFC 8945) with HMAC-SHA1, -SHA256,
///     -SHA384 and -SHA512 (see <see cref="TsigAlgorithm" />). The sign methods and the verify
///     methods differ only in the <see cref="TsigDigestKindEnum" /> they pass to
///     <see cref="TsigCodec.BuildDigestInput" />, so each group shares one core method and both
///     groups share the digest construction.
///     <para>
///         Verification follows the order of RFC 8945 §5.2: algorithm and key, MAC length, MAC,
///         then time. The time check comes last so that BADTIME, which is answered with a signed
///         reply, is only ever sent to a sender that proved it holds the key.
///     </para>
///     <para>
///         Keys named by clustering (a peer's key or an unused invite key) are not ordinary TSIG
///         keys: they are refused for signing and verifying here and work only on the cluster
///         channel.
///     </para>
/// </summary>
/// <param name="tsigKeyRepository">The repository this service looks up pre-shared keys from.</param>
/// <param name="secretProtector">The protector that decrypts a key's shared secret.</param>
/// <param name="options">
///     The signing settings. Read through <see cref="IOptionsMonitor{TOptions}" /> on every
///     signature, because <see cref="IOptions{TOptions}" /> would keep returning the value
///     cached at startup and a configuration change would need a restart.
/// </param>
/// <param name="keyCache">The shared key lookup cache.</param>
/// <param name="timeProvider">The clock for Time Signed and the Fudge window.</param>
public class TsigService(
    ITsigKeyRepository tsigKeyRepository,
    TsigSecretProtector secretProtector,
    IOptionsMonitor<TsigServiceOptions> options,
    TsigKeyLookupCache keyCache,
    TimeProvider timeProvider
) : ITsigService
{
    /// <summary>
    ///     This constant contains the shortest MAC accepted. Truncated MACs (RFC 8945 §5.2.2.1) are
    ///     not supported: a MAC shorter than this is malformed (FORMERR) and one between this and
    ///     the full digest length is BADTRUNC.
    /// </summary>
    private const int MinimumMacLength = 16;

    /// <inheritdoc />
    public Task<TsigVerificationResult> VerifyAsync(
        byte[] rawMessage,
        int tsigRecordStartOffset,
        DnsMessage message,
        CancellationToken stoppingToken = default
    ) =>
        VerifyCoreAsync(
            rawMessage,
            tsigRecordStartOffset,
            message,
            TsigDigestKindEnum.Request,
            chainMac: null,
            unsignedMessages: null,
            expectedKeyName: null,
            stoppingToken
        );

    /// <inheritdoc />
    public Task<TsigVerificationResult> VerifyResponseAsync(
        byte[] rawMessage,
        int tsigRecordStartOffset,
        DnsMessage message,
        byte[] requestMac,
        string expectedKeyName,
        CancellationToken stoppingToken = default
    ) =>
        VerifyCoreAsync(
            rawMessage,
            tsigRecordStartOffset,
            message,
            TsigDigestKindEnum.FirstResponse,
            requestMac,
            unsignedMessages: null,
            expectedKeyName,
            stoppingToken
        );

    /// <inheritdoc />
    public Task<TsigVerificationResult> VerifyContinuationAsync(
        byte[] rawMessage,
        int tsigRecordStartOffset,
        DnsMessage message,
        byte[] priorMac,
        IReadOnlyList<byte[]> unsignedMessageBytesSincePriorTsig,
        string expectedKeyName,
        CancellationToken stoppingToken = default
    ) =>
        VerifyCoreAsync(
            rawMessage,
            tsigRecordStartOffset,
            message,
            TsigDigestKindEnum.Continuation,
            priorMac,
            unsignedMessageBytesSincePriorTsig,
            expectedKeyName,
            stoppingToken
        );

    /// <inheritdoc />
    public Task<DnsMessage> SignAsync(
        DnsMessage message,
        string keyName,
        ushort originalId,
        CancellationToken stoppingToken = default
    ) =>
        SignCoreAsync(
            message,
            keyName,
            originalId,
            TsigDigestKindEnum.Request,
            chainMac: null,
            stoppingToken
        );

    /// <inheritdoc />
    public Task<DnsMessage> SignResponseAsync(
        DnsMessage message,
        string keyName,
        ushort originalId,
        byte[] requestMac,
        CancellationToken stoppingToken = default
    ) =>
        SignCoreAsync(
            message,
            keyName,
            originalId,
            TsigDigestKindEnum.FirstResponse,
            requestMac,
            stoppingToken
        );

    /// <inheritdoc />
    public Task<DnsMessage> SignContinuationAsync(
        DnsMessage message,
        string keyName,
        ushort originalId,
        byte[] priorMac,
        CancellationToken stoppingToken = default
    ) =>
        // The outbound path signs every message, so there are never unsigned messages to add
        // after the prior MAC (unlike VerifyContinuationAsync).
        SignCoreAsync(
            message,
            keyName,
            originalId,
            TsigDigestKindEnum.Continuation,
            priorMac,
            stoppingToken
        );

    /// <inheritdoc />
    public async Task<DnsMessage> SignErrorResponseAsync(
        DnsMessage response,
        DnsTsigOptions requestTsig,
        TsigVerificationResult failure,
        CancellationToken stoppingToken = default
    )
    {
        // RFC 8945 §5.3.2: a request that is not a well-formed TSIG gets a plain FORMERR.
        if (failure.Error == DnsResponseCodeEnum.FormatError)
        {
            response.ResponseCode = DnsResponseCodeEnum.FormatError;
            response.Tsig = null;
            return response;
        }

        // The extended TSIG codes (16 and up) do not fit the four-bit header field, so the header
        // carries NOTAUTH (RFC 8945 §5.3) and the TSIG record carries the code.
        response.ResponseCode = DnsResponseCodeEnum.NotAuthoritative;
        ulong now = (ulong)timeProvider.GetUtcNow().ToUnixTimeSeconds();

        if (failure.Error == DnsResponseCodeEnum.BadTime && failure.Key is not null)
        {
            // RFC 8945 §5.2.3: only a sender that proved it holds the key learns the server time.
            // The reply is signed over the request MAC; Time Signed repeats the request's, and
            // Other Data carries the server time.
            byte[] other = new byte[6];
            for (int i = 0; i < 6; i++)
                other[i] = (byte)(now >> ((5 - i) * 8));

            return await SignWithKeyAsync(
                response,
                failure.Key,
                response.Id,
                TsigDigestKindEnum.FirstResponse,
                requestTsig.Mac,
                requestTsig.TimeSigned,
                DnsResponseCodeEnum.BadTime,
                other,
                stoppingToken
            );
        }

        // RFC 8945 §5.3.2: an error reply for an unknown key or a bad MAC must not be signed (the
        // MAC would prove nothing, and a signature would give an attacker an oracle).
        response.Tsig = new DnsTsigOptions(
            requestTsig.KeyName,
            requestTsig.AlgorithmName,
            now,
            requestTsig.Fudge,
            [],
            response.Id,
            failure.Error,
            []
        );

        return response;
    }

    /// <summary>
    ///     This method does the work for all three verify methods, in the order of RFC 8945 §5.2:
    ///     a supported algorithm, a known, enabled, non-cluster key of that algorithm (and, when
    ///     <paramref name="expectedKeyName" /> is set, that exact key), the MAC length, the MAC (compared in constant time) and finally the time window.
    /// </summary>
    private async Task<TsigVerificationResult> VerifyCoreAsync(
        byte[] rawMessage,
        int tsigRecordStartOffset,
        DnsMessage message,
        TsigDigestKindEnum kind,
        byte[] chainMac,
        IReadOnlyList<byte[]> unsignedMessages,
        string expectedKeyName,
        CancellationToken stoppingToken
    )
    {
        if (message.Tsig is null)
            throw new ArgumentException(
                "The message carries no TSIG record to verify.",
                nameof(message)
            );

        DnsTsigOptions tsig = message.Tsig;

        // An unsupported algorithm is reported as BADKEY, whether or not the key exists.
        TsigAlgorithm algorithm = TsigAlgorithm.Find(tsig.AlgorithmName);
        if (algorithm is null)
            return TsigVerificationResult.Failure(DnsResponseCodeEnum.BadKey);

        // A reply to a request signed with one key must be signed with that key. Another enabled
        // key is as wrong as an unknown one, so it is BADKEY too (names compare without case).
        if (expectedKeyName is not null && !tsig.KeyName.Equals(DnsName.Parse(expectedKeyName)))
            return TsigVerificationResult.Failure(DnsResponseCodeEnum.BadKey);

        TsigKeyEntity key = await keyCache.FindAsync(
            tsig.KeyName.ToString(),
            tsigKeyRepository,
            stoppingToken
        );

        // A disabled key, a cluster key and a key of another algorithm are reported the same as
        // an unknown one (BADKEY).
        if (!IsUsable(key) || TsigAlgorithm.Find(key.AlgorithmName) != algorithm)
            return TsigVerificationResult.Failure(DnsResponseCodeEnum.BadKey);

        // A wrong-length MAC can never match, so skip the MAC computation.
        if (tsig.Mac.Length > algorithm.DigestLength || tsig.Mac.Length < MinimumMacLength)
            return TsigVerificationResult.Failure(DnsResponseCodeEnum.FormatError);

        if (tsig.Mac.Length < algorithm.DigestLength)
            return TsigVerificationResult.Failure(DnsResponseCodeEnum.BadTruncation);

        byte[] secret = secretProtector.Unprotect(key.SecretEncrypted);
        byte[] signedMessageBytes = TsigCodec.ExtractSignedMessageBytes(
            rawMessage,
            tsigRecordStartOffset,
            tsig.OriginalId
        );
        byte[] expectedMac = algorithm.ComputeMac(
            secret,
            TsigCodec.BuildDigestInput(kind, signedMessageBytes, tsig, chainMac, unsignedMessages)
        );

        // A comparison that stops at the first mismatch would leak through timing how many
        // leading bytes are right, letting a MAC be guessed byte by byte.
        if (!CryptographicOperations.FixedTimeEquals(expectedMac, tsig.Mac))
            return TsigVerificationResult.Failure(
                DnsResponseCodeEnum.BadVersionOrBadSignature,
                key
            );

        // RFC 8945 §5.2.3: the clocks may differ by up to the signer's Fudge. The MAC covers
        // Time Signed and Fudge, so neither can be altered without breaking the signature.
        long nowUnixSeconds = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        long skewSeconds = Math.Abs(nowUnixSeconds - (long)tsig.TimeSigned);

        return skewSeconds > tsig.Fudge
            ? TsigVerificationResult.Failure(DnsResponseCodeEnum.BadTime, key)
            : TsigVerificationResult.Success(key);
    }

    /// <summary>
    ///     This method tells whether a looked-up key may sign or verify: it exists, is enabled and
    ///     is an operator key. A key clustering created (a peer's key, or an invite key that is
    ///     not used up yet) belongs to the cluster channel only; letting it sign or verify zone
    ///     transfers and updates would hand a peer rights over every zone.
    /// </summary>
    private static bool IsUsable(TsigKeyEntity key) =>
        key is { IsDisabled: false, ClusterNodeId: null, ClusterInviteExpiresAtUtc: null };

    /// <summary>
    ///     This method does the work for all three sign methods: it looks up the key, then signs.
    /// </summary>
    private async Task<DnsMessage> SignCoreAsync(
        DnsMessage message,
        string keyName,
        ushort originalId,
        TsigDigestKindEnum kind,
        byte[] chainMac,
        CancellationToken stoppingToken
    )
    {
        TsigKeyEntity key =
            await keyCache.FindAsync(keyName, tsigKeyRepository, stoppingToken)
            ?? throw new InvalidOperationException($"No TSIG key named '{keyName}' exists.");

        if (key.IsDisabled)
            throw new InvalidOperationException($"The TSIG key named '{keyName}' is disabled.");

        if (!IsUsable(key))
            throw new InvalidOperationException(
                $"The TSIG key named '{keyName}' belongs to clustering and cannot sign DNS messages."
            );

        return await SignWithKeyAsync(
            message,
            key,
            originalId,
            kind,
            chainMac,
            (ulong)timeProvider.GetUtcNow().ToUnixTimeSeconds(),
            DnsResponseCodeEnum.NoError,
            [],
            stoppingToken
        );
    }

    /// <summary>
    ///     This method computes the MAC with a key already looked up and attaches the TSIG record.
    ///     The MAC covers the message without its TSIG record, so it serializes before assigning
    ///     <see cref="DnsMessage.Tsig" />.
    /// </summary>
    private Task<DnsMessage> SignWithKeyAsync(
        DnsMessage message,
        TsigKeyEntity key,
        ushort originalId,
        TsigDigestKindEnum kind,
        byte[] chainMac,
        ulong timeSigned,
        DnsResponseCodeEnum error,
        byte[] otherData,
        CancellationToken stoppingToken
    )
    {
        stoppingToken.ThrowIfCancellationRequested();

        TsigAlgorithm algorithm =
            TsigAlgorithm.Find(key.AlgorithmName)
            ?? throw new InvalidOperationException(
                $"The TSIG key named '{key.Name}' uses the unsupported algorithm '{key.AlgorithmName}'."
            );
        byte[] secret = secretProtector.Unprotect(key.SecretEncrypted);

        byte[] messageBytes = TsigCodec.ApplyOriginalId(
            DnsMessageWriter.Write(message),
            originalId
        );

        DnsTsigOptions unsignedTsig = new(
            DnsName.Parse(key.Name),
            algorithm.Name,
            timeSigned,
            options.CurrentValue.DefaultFudgeSeconds,
            [],
            originalId,
            error,
            otherData
        );

        byte[] mac = algorithm.ComputeMac(
            secret,
            TsigCodec.BuildDigestInput(kind, messageBytes, unsignedTsig, chainMac, null)
        );

        message.Tsig = new DnsTsigOptions(
            unsignedTsig.KeyName,
            unsignedTsig.AlgorithmName,
            unsignedTsig.TimeSigned,
            unsignedTsig.Fudge,
            mac,
            unsignedTsig.OriginalId,
            unsignedTsig.Error,
            unsignedTsig.OtherData
        );

        return Task.FromResult(message);
    }
}
