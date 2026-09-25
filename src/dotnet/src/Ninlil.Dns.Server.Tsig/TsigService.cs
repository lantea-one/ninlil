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
///     This class implements <see cref="ITsigService" /> with HMAC-SHA256 (RFC 8945), the only
///     TSIG algorithm Ninlil supports. The three sign methods and three verify methods differ
///     only in what goes before the message bytes in the digest and which TSIG fields follow it,
///     so each group shares one core method.
/// </summary>
/// <param name="tsigKeyRepository">The repository this service looks up pre-shared keys from.</param>
/// <param name="secretProtector">The protector that decrypts a key's shared secret.</param>
/// <param name="options">
///     The signing settings. Read through <see cref="IOptionsMonitor{TOptions}" /> on every
///     signature, because <see cref="IOptions{TOptions}" /> would keep returning the value
///     cached at startup and a configuration change would need a restart.
/// </param>
public class TsigService(
    ITsigKeyRepository tsigKeyRepository,
    TsigSecretProtector secretProtector,
    IOptionsMonitor<TsigServiceOptions> options
) : ITsigService
{
    /// <summary>
    ///     This constant contains the length, in bytes, of an HMAC-SHA256 digest. A MAC of any
    ///     other length is rejected before the key lookup; truncated MACs (RFC 8945 §5.2.2.1) are
    ///     not supported.
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
            // RFC 8945 §5.3.1: the prior MAC, then the bytes of any unsigned messages received
            // since it, in order.
            macPrefixBytes:
            [
                .. priorMac ?? [],
                .. (unsignedMessageBytesSincePriorTsig ?? []).SelectMany(bytes => bytes),
            ],
            // Later messages cover only the TSIG Timers; see TsigCodec.BuildTimersOnly.
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
            // The outbound path signs every message, so there are never unsigned messages to add
            // after the prior MAC (unlike VerifyContinuationAsync).
            macPrefixBytes: priorMac ?? [],
            buildVariables: tsig => TsigCodec.BuildTimersOnly(tsig.TimeSigned, tsig.Fudge),
            stoppingToken
        );

    /// <summary>
    ///     This method does the work for all three verify methods. It checks, in order: a supported
    ///     algorithm, the MAC length, a known and enabled key, and the time window (Time Signed
    ///     within Fudge of now). It then computes the expected MAC from
    ///     <paramref name="macPrefixBytes" />, the signed message bytes and
    ///     <paramref name="buildVariables" />, and compares it with the one received.
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

        // An unsupported algorithm is reported as BADKEY, whether or not the key exists.
        if (tsig.AlgorithmName != DnsTsigOptions.HmacSha256AlgorithmName)
            return TsigVerificationResult.Failure(DnsResponseCodeEnum.BadKey);

        // A wrong-length MAC can never match, so skip the key lookup and MAC computation.
        if (tsig.Mac.Length != HmacSha256DigestLength)
            return TsigVerificationResult.Failure(DnsResponseCodeEnum.BadVersionOrBadSignature);

        string keyName = tsig.KeyName.ToString().TrimEnd('.');
        TsigKeyEntity key = await tsigKeyRepository.FindByNameAsync(keyName, stoppingToken);

        // A disabled key is reported the same as an unknown one (BADKEY).
        if (key is null or { IsDisabled: true })
            return TsigVerificationResult.Failure(DnsResponseCodeEnum.BadKey);

        // RFC 8945 §5.2.3: the clocks may differ by up to the signer's Fudge. The MAC covers
        // Time Signed and Fudge, so neither can be altered without breaking the signature.
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

        // A comparison that stops at the first mismatch would leak through timing how many
        // leading bytes are right, letting a MAC be guessed byte by byte.
        return CryptographicOperations.FixedTimeEquals(expectedMac, tsig.Mac)
            ? TsigVerificationResult.Success(key)
            : TsigVerificationResult.Failure(DnsResponseCodeEnum.BadVersionOrBadSignature, key);
    }

    /// <summary>
    ///     This method does the work for all three sign methods: it looks up and decrypts the key,
    ///     then computes the MAC over <paramref name="macPrefixBytes" />, the message bytes and
    ///     <paramref name="buildVariables" />, and attaches the TSIG record.
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

        // The MAC covers the message without its TSIG record, so serialize before assigning it.
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
