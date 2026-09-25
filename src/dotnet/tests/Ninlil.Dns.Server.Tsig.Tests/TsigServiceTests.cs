using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig.Tests;

/// <summary>
///     This class tests <see cref="TsigService" /> (RFC 8945) against the EF Core key repository
///     on a temporary SQLite database (<see cref="TsigServiceTestFixture" />), with secrets
///     encrypted by Data Protection. It covers sign/verify round trips, tampering, BADTIME,
///     BADKEY (unknown, disabled, unsupported algorithm), wrong MAC length, and the request-MAC
///     and prior-MAC chaining used by multi-message zone transfers.
/// </summary>
public class TsigServiceTests(TsigServiceTestFixture fixture)
    : IClassFixture<TsigServiceTestFixture>
{
    private async Task<TsigKeyEntity> CreateKeyAsync(bool isDisabled = false)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ITsigKeyRepository keyRepository =
            scope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        IDataProtectionProvider dataProtectionProvider =
            scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        IDataProtector protector = dataProtectionProvider.CreateProtector(
            "Ninlil.Dns.Server.Tsig.SharedSecret"
        );

        byte[] rawSecret = new byte[32];
        Random.Shared.NextBytes(rawSecret);

        TsigKeyEntity key = new()
        {
            Id = Guid.NewGuid(),
            Name = $"test-key-{Guid.NewGuid()}",
            AlgorithmName = "hmac-sha256.",
            SecretEncrypted = protector.Protect(Convert.ToBase64String(rawSecret)),
            IsDisabled = isDisabled,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await keyRepository.AddAsync(key);
        await unitOfWork.SaveChangesAsync();

        return key;
    }

    /// <summary>
    ///     This method creates and stores a key like <see cref="CreateKeyAsync" />, and also
    ///     returns the raw secret.
    ///     <see cref="VerifyContinuationToleratesARealUnsignedIntermediateMessage" /> needs it to
    ///     compute a digest by hand instead of through <see cref="TsigService" />.
    /// </summary>
    private async Task<(TsigKeyEntity Key, byte[] Secret)> CreateKeyWithSecretAsync()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ITsigKeyRepository keyRepository =
            scope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        IDataProtectionProvider dataProtectionProvider =
            scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        IDataProtector protector = dataProtectionProvider.CreateProtector(
            "Ninlil.Dns.Server.Tsig.SharedSecret"
        );

        byte[] rawSecret = new byte[32];
        Random.Shared.NextBytes(rawSecret);

        TsigKeyEntity key = new()
        {
            Id = Guid.NewGuid(),
            Name = $"test-key-{Guid.NewGuid()}",
            AlgorithmName = "hmac-sha256.",
            SecretEncrypted = protector.Protect(Convert.ToBase64String(rawSecret)),
            IsDisabled = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await keyRepository.AddAsync(key);
        await unitOfWork.SaveChangesAsync();

        return (key, rawSecret);
    }

    private static DnsMessage BuildMessage(ushort id) =>
        new()
        {
            Id = id,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("example.com"),
                    DnsResourceRecordTypeEnum.Soa,
                    DnsClassEnum.Internet
                ),
            },
        };

    /// <summary>
    ///     This method serializes a signed message and parses it again, as a transport would, so
    ///     the raw bytes and TSIG record offset that <see cref="ITsigService.VerifyAsync" /> needs
    ///     are available.
    /// </summary>
    private static (byte[] RawMessage, int TsigRecordStartOffset, DnsMessage Reparsed) Reparse(
        DnsMessage signedMessage
    )
    {
        byte[] rawMessage = DnsMessageWriter.Write(signedMessage);
        DnsMessage reparsed = DnsMessageReader.Read(rawMessage, out int tsigRecordStartOffset);

        return (rawMessage, tsigRecordStartOffset, reparsed);
    }

    [Fact]
    public async Task SignThenVerifyRoundTripSucceeds()
    {
        TsigKeyEntity key = await CreateKeyAsync();
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();

        DnsMessage signed = await tsigService.SignAsync(BuildMessage(1), key.Name, 1);
        (byte[] rawMessage, int tsigOffset, DnsMessage reparsed) = Reparse(signed);

        TsigVerificationResult result = await tsigService.VerifyAsync(
            rawMessage,
            tsigOffset,
            reparsed
        );

        Assert.True(result.IsValid);
        Assert.Equal(DnsResponseCodeEnum.NoError, result.Error);
    }

    [Fact]
    public async Task VerifyFailsWhenTheSignedMessageIsTamperedWith()
    {
        TsigKeyEntity key = await CreateKeyAsync();
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();

        DnsMessage signed = await tsigService.SignAsync(BuildMessage(2), key.Name, 2);
        (byte[] rawMessage, int tsigOffset, DnsMessage reparsed) = Reparse(signed);

        // Flip a byte in the question name, inside the signed part of the message.
        rawMessage[13] ^= 0xFF;

        TsigVerificationResult result = await tsigService.VerifyAsync(
            rawMessage,
            tsigOffset,
            reparsed
        );

        Assert.False(result.IsValid);
        Assert.Equal(DnsResponseCodeEnum.BadVersionOrBadSignature, result.Error);
    }

    [Fact]
    public async Task VerifyFailsWithBadTimeWhenTheTimestampIsOutsideTheFudgeWindow()
    {
        TsigKeyEntity key = await CreateKeyAsync();
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();

        // TsigService checks the time before computing the MAC, so a correctly sized but
        // meaningless MAC is enough to reach the check.
        DnsMessage message = BuildMessage(3);
        message.Tsig = new DnsTsigOptions(
            DnsName.Parse(key.Name),
            DnsTsigOptions.HmacSha256AlgorithmName,
            (ulong)DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds(),
            300,
            new byte[32],
            message.Id,
            DnsResponseCodeEnum.NoError,
            []
        );
        (byte[] rawMessage, int tsigOffset, DnsMessage reparsed) = Reparse(message);

        TsigVerificationResult result = await tsigService.VerifyAsync(
            rawMessage,
            tsigOffset,
            reparsed
        );

        Assert.False(result.IsValid);
        Assert.Equal(DnsResponseCodeEnum.BadTime, result.Error);
    }

    [Fact]
    public async Task VerifyFailsWithBadKeyForAnUnknownKeyName()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();

        DnsMessage message = BuildMessage(4);
        message.Tsig = new DnsTsigOptions(
            DnsName.Parse($"nonexistent-key-{Guid.NewGuid()}"),
            DnsTsigOptions.HmacSha256AlgorithmName,
            (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            300,
            new byte[32],
            message.Id,
            DnsResponseCodeEnum.NoError,
            []
        );
        (byte[] rawMessage, int tsigOffset, DnsMessage reparsed) = Reparse(message);

        TsigVerificationResult result = await tsigService.VerifyAsync(
            rawMessage,
            tsigOffset,
            reparsed
        );

        Assert.False(result.IsValid);
        Assert.Equal(DnsResponseCodeEnum.BadKey, result.Error);
    }

    [Fact]
    public async Task VerifyFailsWithBadKeyForADisabledKey()
    {
        TsigKeyEntity key = await CreateKeyAsync();

        // Sign while the key is enabled, then disable it.
        byte[] rawMessage;
        int tsigOffset;
        DnsMessage reparsed;

        await using (AsyncServiceScope signScope = fixture.Services.CreateAsyncScope())
        {
            ITsigService signingService =
                signScope.ServiceProvider.GetRequiredService<ITsigService>();
            DnsMessage signed = await signingService.SignAsync(BuildMessage(5), key.Name, 5);
            (rawMessage, tsigOffset, reparsed) = Reparse(signed);
        }

        await using (AsyncServiceScope disableScope = fixture.Services.CreateAsyncScope())
        {
            ITsigKeyRepository keyRepository =
                disableScope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();
            IUnitOfWork unitOfWork = disableScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            TsigKeyEntity storedKey = await keyRepository.GetByIdAsync(key.Id);
            storedKey.IsDisabled = true;
            keyRepository.Update(storedKey);
            await unitOfWork.SaveChangesAsync();
        }

        // Verify in a new scope: signScope's DbContext still tracks the old, enabled copy of the
        // key and would return it instead of re-reading the database.
        await using AsyncServiceScope verifyScope = fixture.Services.CreateAsyncScope();
        ITsigService verifyingService =
            verifyScope.ServiceProvider.GetRequiredService<ITsigService>();
        TsigVerificationResult result = await verifyingService.VerifyAsync(
            rawMessage,
            tsigOffset,
            reparsed
        );

        Assert.False(result.IsValid);
        Assert.Equal(DnsResponseCodeEnum.BadKey, result.Error);
    }

    [Fact]
    public async Task VerifyFailsWithBadKeyForAnUnsupportedAlgorithm()
    {
        TsigKeyEntity key = await CreateKeyAsync();
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();

        DnsMessage message = BuildMessage(6);
        message.Tsig = new DnsTsigOptions(
            DnsName.Parse(key.Name),
            DnsName.Parse("hmac-sha1"), // Not supported; only HMAC-SHA256 is.
            (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            300,
            new byte[32],
            message.Id,
            DnsResponseCodeEnum.NoError,
            []
        );
        (byte[] rawMessage, int tsigOffset, DnsMessage reparsed) = Reparse(message);

        TsigVerificationResult result = await tsigService.VerifyAsync(
            rawMessage,
            tsigOffset,
            reparsed
        );

        Assert.False(result.IsValid);
        Assert.Equal(DnsResponseCodeEnum.BadKey, result.Error);
    }

    [Fact]
    public async Task VerifyFailsForAWrongLengthMac()
    {
        TsigKeyEntity key = await CreateKeyAsync();
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();

        DnsMessage message = BuildMessage(7);
        message.Tsig = new DnsTsigOptions(
            DnsName.Parse(key.Name),
            DnsTsigOptions.HmacSha256AlgorithmName,
            (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            300,
            new byte[16], // HMAC-SHA256 produces 32 bytes.
            message.Id,
            DnsResponseCodeEnum.NoError,
            []
        );
        (byte[] rawMessage, int tsigOffset, DnsMessage reparsed) = Reparse(message);

        TsigVerificationResult result = await tsigService.VerifyAsync(
            rawMessage,
            tsigOffset,
            reparsed
        );

        Assert.False(result.IsValid);
        Assert.Equal(DnsResponseCodeEnum.BadVersionOrBadSignature, result.Error);
    }

    [Fact]
    public async Task SignResponseThenVerifyResponseRoundTripSucceeds()
    {
        TsigKeyEntity key = await CreateKeyAsync();
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();

        byte[] requestMac = new byte[32];
        Random.Shared.NextBytes(requestMac);

        DnsMessage signed = await tsigService.SignResponseAsync(
            BuildMessage(8),
            key.Name,
            8,
            requestMac
        );
        (byte[] rawMessage, int tsigOffset, DnsMessage reparsed) = Reparse(signed);

        TsigVerificationResult result = await tsigService.VerifyResponseAsync(
            rawMessage,
            tsigOffset,
            reparsed,
            requestMac
        );

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task VerifyResponseFailsWhenTheRequestMacDoesNotMatchWhatWasSigned()
    {
        TsigKeyEntity key = await CreateKeyAsync();
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();

        byte[] realRequestMac = new byte[32];
        Random.Shared.NextBytes(realRequestMac);
        byte[] wrongRequestMac = new byte[32];
        Random.Shared.NextBytes(wrongRequestMac);

        DnsMessage signed = await tsigService.SignResponseAsync(
            BuildMessage(9),
            key.Name,
            9,
            realRequestMac
        );
        (byte[] rawMessage, int tsigOffset, DnsMessage reparsed) = Reparse(signed);

        // RFC 8945 §5.3 binds a response to its request, so verifying against a different
        // request's MAC must fail like a tampered message.
        TsigVerificationResult result = await tsigService.VerifyResponseAsync(
            rawMessage,
            tsigOffset,
            reparsed,
            wrongRequestMac
        );

        Assert.False(result.IsValid);
        Assert.Equal(DnsResponseCodeEnum.BadVersionOrBadSignature, result.Error);
    }

    [Fact]
    public async Task SignContinuationThenVerifyContinuationRoundTripSucceeds()
    {
        TsigKeyEntity key = await CreateKeyAsync();
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();

        byte[] priorMac = new byte[32];
        Random.Shared.NextBytes(priorMac);

        DnsMessage signed = await tsigService.SignContinuationAsync(
            BuildMessage(10),
            key.Name,
            10,
            priorMac
        );
        (byte[] rawMessage, int tsigOffset, DnsMessage reparsed) = Reparse(signed);

        TsigVerificationResult result = await tsigService.VerifyContinuationAsync(
            rawMessage,
            tsigOffset,
            reparsed,
            priorMac,
            []
        );

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task VerifyContinuationRejectsADeliberatelyBrokenChain()
    {
        TsigKeyEntity key = await CreateKeyAsync();
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();

        byte[] realPriorMac = new byte[32];
        Random.Shared.NextBytes(realPriorMac);

        DnsMessage signed = await tsigService.SignContinuationAsync(
            BuildMessage(11),
            key.Name,
            11,
            realPriorMac
        );
        (byte[] rawMessage, int tsigOffset, DnsMessage reparsed) = Reparse(signed);

        // A wrong prior MAC looks like a dropped, reordered or spliced-in message in a transfer,
        // and must be rejected.
        byte[] wrongPriorMac = new byte[32];
        Random.Shared.NextBytes(wrongPriorMac);

        TsigVerificationResult result = await tsigService.VerifyContinuationAsync(
            rawMessage,
            tsigOffset,
            reparsed,
            wrongPriorMac,
            []
        );

        Assert.False(result.IsValid);
        Assert.Equal(DnsResponseCodeEnum.BadVersionOrBadSignature, result.Error);
    }

    [Fact]
    public async Task FullThreeMessageSequenceChainsAndVerifiesEndToEnd()
    {
        TsigKeyEntity key = await CreateKeyAsync();
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();

        // The same chain ZoneTransferService signs and ZoneTransferPullOverStream verifies for a
        // multi-message AXFR/IXFR: message 1 chains from the request MAC, each later message from
        // the previous signed message.
        byte[] requestMac = new byte[32];
        Random.Shared.NextBytes(requestMac);

        DnsMessage signed1 = await tsigService.SignResponseAsync(
            BuildMessage(12),
            key.Name,
            12,
            requestMac
        );
        (byte[] rawMessage1, int tsigOffset1, DnsMessage reparsed1) = Reparse(signed1);
        TsigVerificationResult result1 = await tsigService.VerifyResponseAsync(
            rawMessage1,
            tsigOffset1,
            reparsed1,
            requestMac
        );
        Assert.True(result1.IsValid);

        DnsMessage signed2 = await tsigService.SignContinuationAsync(
            BuildMessage(12),
            key.Name,
            12,
            reparsed1.Tsig.Mac
        );
        (byte[] rawMessage2, int tsigOffset2, DnsMessage reparsed2) = Reparse(signed2);
        TsigVerificationResult result2 = await tsigService.VerifyContinuationAsync(
            rawMessage2,
            tsigOffset2,
            reparsed2,
            reparsed1.Tsig.Mac,
            []
        );
        Assert.True(result2.IsValid);

        DnsMessage signed3 = await tsigService.SignContinuationAsync(
            BuildMessage(12),
            key.Name,
            12,
            reparsed2.Tsig.Mac
        );
        (byte[] rawMessage3, int tsigOffset3, DnsMessage reparsed3) = Reparse(signed3);
        TsigVerificationResult result3 = await tsigService.VerifyContinuationAsync(
            rawMessage3,
            tsigOffset3,
            reparsed3,
            reparsed2.Tsig.Mac,
            []
        );
        Assert.True(result3.IsValid);

        // Verifying message 3 against message 1's MAC, as if message 2 were missing, must fail.
        TsigVerificationResult skippedResult = await tsigService.VerifyContinuationAsync(
            rawMessage3,
            tsigOffset3,
            reparsed3,
            reparsed1.Tsig.Mac,
            []
        );
        Assert.False(skippedResult.IsValid);
    }

    [Fact]
    public async Task VerifyContinuationToleratesARealUnsignedIntermediateMessage()
    {
        (TsigKeyEntity key, byte[] secret) = await CreateKeyWithSecretAsync();
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();

        // Message 1: a signed first message.
        byte[] requestMac = new byte[32];
        Random.Shared.NextBytes(requestMac);
        DnsMessage signed1 = await tsigService.SignResponseAsync(
            BuildMessage(13),
            key.Name,
            13,
            requestMac
        );
        (_, _, DnsMessage reparsed1) = Reparse(signed1);

        // Message 2: unsigned. RFC 8945 §5.3.1 lets a peer leave up to 99 messages in a row
        // unsigned, as long as the first and last are signed.
        byte[] unsignedMessage2Bytes = DnsMessageWriter.Write(BuildMessage(13));

        // Message 3: signed. Its digest is message 1's MAC, then message 2's bytes, then message
        // 3's bytes, then the timers (RFC 8945 §5.3.1). It is computed by hand here rather than
        // through TsigService. Like TsigService, it uses the raw MAC without its size field.
        DnsMessage message3 = BuildMessage(13);
        byte[] message3Bytes = DnsMessageWriter.Write(message3);
        ulong timeSigned = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ushort fudge = 300;
        byte[] timersOnly = TsigCodec.BuildTimersOnly(timeSigned, fudge);
        byte[] expectedMacInput =
        [
            .. reparsed1.Tsig.Mac,
            .. unsignedMessage2Bytes,
            .. message3Bytes,
            .. timersOnly,
        ];
        byte[] handComputedMac = HMACSHA256.HashData(secret, expectedMacInput);

        message3.Tsig = new DnsTsigOptions(
            DnsName.Parse(key.Name),
            DnsTsigOptions.HmacSha256AlgorithmName,
            timeSigned,
            fudge,
            handComputedMac,
            message3.Id,
            DnsResponseCodeEnum.NoError,
            []
        );
        (byte[] rawMessage3, int tsigOffset3, DnsMessage reparsed3) = Reparse(message3);

        TsigVerificationResult result = await tsigService.VerifyContinuationAsync(
            rawMessage3,
            tsigOffset3,
            reparsed3,
            reparsed1.Tsig.Mac,
            [unsignedMessage2Bytes]
        );

        Assert.True(result.IsValid);

        // Leaving out message 2's bytes must fail, so the unsigned bytes are part of the digest.
        TsigVerificationResult resultIgnoringGap = await tsigService.VerifyContinuationAsync(
            rawMessage3,
            tsigOffset3,
            reparsed3,
            reparsed1.Tsig.Mac,
            []
        );

        Assert.False(resultIgnoringGap.IsValid);
    }
}
