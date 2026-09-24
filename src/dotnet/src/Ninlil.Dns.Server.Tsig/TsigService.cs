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
///     This class implements <see cref="ITsigService" />, computing and checking HMAC-SHA256
///     message authentication codes per RFC 8945 -- the only algorithm Ninlil implements.
/// </summary>
/// <param name="tsigKeyRepository">The repository this service looks up pre-shared keys from.</param>
/// <param name="secretProtector">
///     The protector used to decrypt a key's shared secret before use. A MAC cannot be computed
///     from a one-way hash the way a password can be checked, so the secret has to be
///     recoverable, not just comparable.
/// </param>
/// <param name="options">
///     This service's own configuration, such as the default signing fudge -- bound via
///     <see cref="IOptionsMonitor{TOptions}" /> and read fresh, via <c>options.CurrentValue</c>,
///     on every single <see cref="SignAsync" /> call, so a configuration write reaches the very
///     next message this (scoped) service signs, with no restart -- even though a fresh instance
///     is itself constructed per scope, the plain, snapshotted <see cref="IOptions{TOptions}" />
///     this class used before this fix would still have handed every one of those fresh
///     instances back the exact same process-wide-cached value.
/// </param>
public class TsigService(
    ITsigKeyRepository tsigKeyRepository,
    TsigSecretProtector secretProtector,
    IOptionsMonitor<TsigServiceOptions> options
) : ITsigService
{
    /// <summary>
    ///     This constant contains the length, in bytes, of an HMAC-SHA256 digest. A MAC of any
    ///     other length cannot possibly be a valid HMAC-SHA256 signature, so it is rejected
    ///     before ever computing a comparison MAC -- Ninlil does not support RFC 8945 §5.3's
    ///     optional truncated-MAC allowance.
    /// </summary>
    private const int HmacSha256DigestLength = 32;

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
            macPrefixBytes: [],
            buildVariables: TsigCodec.BuildVariables,
            stoppingToken
        );

    /// <inheritdoc />
    public Task<TsigVerificationResult> VerifyResponseAsync(
        byte[] rawMessage,
        int tsigRecordStartOffset,
        DnsMessage message,
        byte[] requestMac,
        CancellationToken stoppingToken = default
    ) =>
        VerifyCoreAsync(
            rawMessage,
            tsigRecordStartOffset,
            message,
            macPrefixBytes: requestMac ?? [],
            buildVariables: TsigCodec.BuildVariables,
            stoppingToken
        );

    /// <inheritdoc />
    public Task<TsigVerificationResult> VerifyContinuationAsync(
        byte[] rawMessage,
        int tsigRecordStartOffset,
        DnsMessage message,
        byte[] priorMac,
        IReadOnlyList<byte[]> unsignedMessageBytesSincePriorTsig,
        CancellationToken stoppingToken = default
    ) =>
        VerifyCoreAsync(
            rawMessage,
            tsigRecordStartOffset,
            message,
            // RFC 8945 §5.3.1's own "DNS Messages" digest component: the running prior MAC,
            // followed by the raw bytes of every message received since (but not including) the
            // one that MAC came from, in order -- empty in the common case where every message in
            // the sequence carries its own TSIG, as this project's own outbound path always does.
            macPrefixBytes:
            [
                .. priorMac ?? [],
                .. (unsignedMessageBytesSincePriorTsig ?? []).SelectMany(bytes => bytes),
            ],
            // Only the Timers -- not the full TSIG Variables -- are covered for a second or later
            // message in the sequence; see TsigCodec.BuildTimersOnly's own doc comment.
            buildVariables: tsig => TsigCodec.BuildTimersOnly(tsig.TimeSigned, tsig.Fudge),
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
            macPrefixBytes: [],
            buildVariables: TsigCodec.BuildVariables,
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
            macPrefixBytes: requestMac ?? [],
            buildVariables: TsigCodec.BuildVariables,
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
        SignCoreAsync(
            message,
            keyName,
            originalId,
            // This project's own outbound zone-transfer path always signs every message in a
            // sequence, so the "DNS Messages" digest component RFC 8945 §5.3.1 defines for a
            // second-or-later message is always just the running prior MAC on its own -- no
            // intervening unsigned message's own bytes ever need folding in here (contrast
            // VerifyContinuationAsync, which still has to tolerate a real-world peer that chooses
            // to leave some messages unsigned instead).
            macPrefixBytes: priorMac ?? [],
            buildVariables: tsig => TsigCodec.BuildTimersOnly(tsig.TimeSigned, tsig.Fudge),
            stoppingToken
        );

    /// <summary>
    ///     This method implements every <see cref="ITsigService" /> verification method: the
    ///     shared checks RFC 8945 §5.2 requires regardless of which digest formula applies
    ///     (recognized algorithm, correctly-sized MAC, a known and enabled key, and the signer's
    ///     clock falling within its own declared tolerance), followed by a digest computed from
    ///     whichever MAC-prefix and TSIG-variables-building rule the caller supplies -- the one
    ///     axis <see cref="VerifyAsync" />/<see cref="VerifyResponseAsync" />/
    ///     <see cref="VerifyContinuationAsync" /> actually differ on.
    /// </summary>
    private async Task<TsigVerificationResult> VerifyCoreAsync(
        byte[] rawMessage,
        int tsigRecordStartOffset,
        DnsMessage message,
        byte[] macPrefixBytes,
        Func<DnsTsigOptions, byte[]> buildVariables,
        CancellationToken stoppingToken
    )
    {
        if (message.Tsig is null)
            throw new ArgumentException(
                "The message carries no TSIG record to verify.",
                nameof(message)
            );

        DnsTsigOptions tsig = message.Tsig;

        // An algorithm this server does not implement can never be verified, regardless of
        // whether a key by the claimed name even exists -- treated the same way BIND and most
        // other implementations treat it in practice: as though the key itself were unusable.
        if (tsig.AlgorithmName != DnsTsigOptions.HmacSha256AlgorithmName)
            return TsigVerificationResult.Failure(DnsResponseCodeEnum.BadKey);

        // A wrong-length MAC can never equal a real HMAC-SHA256 digest, so it is rejected here,
        // before the comparatively expensive key lookup and MAC computation below ever run.
        if (tsig.Mac.Length != HmacSha256DigestLength)
            return TsigVerificationResult.Failure(DnsResponseCodeEnum.BadVersionOrBadSignature);

        string keyName = tsig.KeyName.ToString().TrimEnd('.');
        TsigKeyEntity key = await tsigKeyRepository.FindByNameAsync(keyName, stoppingToken);

        // An unknown key name and a disabled key are reported identically (BadKey) -- a disabled
        // key is meant to behave as though it no longer exists for verification purposes.
        if (key is null or { IsDisabled: true })
            return TsigVerificationResult.Failure(DnsResponseCodeEnum.BadKey);

        // RFC 8945 §5.2 allows the signer's and verifier's clocks to disagree within the signer's
        // own declared tolerance (Fudge), not require them to match exactly -- ordinary clock
        // drift between two independent machines, not a security weakening, since the MAC itself
        // covers both TimeSigned and Fudge and so cannot be forged independently of them.
        long nowUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long skewSeconds = Math.Abs(nowUnixSeconds - (long)tsig.TimeSigned);

        if (skewSeconds > tsig.Fudge)
            return TsigVerificationResult.Failure(DnsResponseCodeEnum.BadTime, key);

        byte[] secret = secretProtector.Unprotect(key.SecretEncrypted);
        byte[] signedMessageBytes = TsigCodec.ExtractSignedMessageBytes(
            rawMessage,
            tsigRecordStartOffset
        );
        byte[] variables = buildVariables(tsig);
        byte[] macInput = [.. macPrefixBytes, .. signedMessageBytes, .. variables];
        byte[] expectedMac = HMACSHA256.HashData(secret, macInput);

        // A byte-by-byte comparison that returns as soon as it finds a mismatch would leak, via
        // response timing, how many leading bytes of an attacker-supplied MAC happen to be
        // correct -- letting a MAC be forged one byte at a time. FixedTimeEquals always compares
        // every byte, in constant time regardless of where (or whether) a mismatch occurs.
        return CryptographicOperations.FixedTimeEquals(expectedMac, tsig.Mac)
            ? TsigVerificationResult.Success(key)
            : TsigVerificationResult.Failure(DnsResponseCodeEnum.BadVersionOrBadSignature, key);
    }

    /// <summary>
    ///     This method implements every <see cref="ITsigService" /> signing method: the shared key
    ///     lookup and MAC computation, parameterized by whichever MAC-prefix and
    ///     TSIG-variables-building rule the caller supplies -- the one axis
    ///     <see cref="SignAsync" />/<see cref="SignResponseAsync" />/
    ///     <see cref="SignContinuationAsync" /> actually differ on.
    /// </summary>
    private async Task<DnsMessage> SignCoreAsync(
        DnsMessage message,
        string keyName,
        ushort originalId,
        byte[] macPrefixBytes,
        Func<DnsTsigOptions, byte[]> buildVariables,
        CancellationToken stoppingToken
    )
    {
        TsigKeyEntity key =
            await tsigKeyRepository.FindByNameAsync(keyName, stoppingToken)
            ?? throw new InvalidOperationException($"No TSIG key named '{keyName}' exists.");

        if (key.IsDisabled)
            throw new InvalidOperationException($"The TSIG key named '{keyName}' is disabled.");

        byte[] secret = secretProtector.Unprotect(key.SecretEncrypted);

        // The MAC is computed over the message exactly as it would be sent without a TSIG
        // record at all, so it is serialized once, before Tsig is ever assigned.
        byte[] messageBytes = DnsMessageWriter.Write(message);

        DnsTsigOptions unsignedTsig = new(
            DnsName.Parse(key.Name),
            DnsTsigOptions.HmacSha256AlgorithmName,
            (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            options.CurrentValue.DefaultFudgeSeconds,
            [],
            originalId,
            DnsResponseCodeEnum.NoError,
            []
        );

        byte[] variables = buildVariables(unsignedTsig);
        byte[] macInput = [.. macPrefixBytes, .. messageBytes, .. variables];
        byte[] mac = HMACSHA256.HashData(secret, macInput);

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

        return message;
    }
}
