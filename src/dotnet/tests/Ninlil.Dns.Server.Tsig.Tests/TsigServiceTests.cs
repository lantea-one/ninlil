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
///     This class tests <see cref="TsigService" /> (RFC 8945) against a real, SQLite-backed key
///     repository and real encrypted secrets -- the same shape as the throwaway smoke test this
///     project was originally verified with, now a permanent, committed regression suite.
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
    ///     This method creates a real, persisted key exactly like <see cref="CreateKeyAsync" />,
    ///     but also hands the test its own raw, unencrypted secret -- needed only by
    ///     <see cref="VerifyContinuationToleratesARealUnsignedIntermediateMessage" />, which has to
    ///     hand-compute an RFC 8945 §5.3.1 digest independently of <see cref="TsigService" /> itself
    ///     to prove that class's own formula matches the RFC, not just that two calls into the
    ///     same class happen to agree with each other.
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
    ///     This method mirrors what a real transport does: sign, serialize, then re-parse (so
    ///     the TSIG record's own starting offset is known), the same round trip
    ///     <see cref="ITsigService.VerifyAsync" /> expects.
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

        // Flip a byte well before the TSIG record itself, inside the signed portion of the
        // message -- the question section's own name.
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

        // RFC 8945 §5.2.2's timestamp check runs before the MAC is ever computed or compared, so
        // an otherwise-garbage, but correctly-sized, MAC is enough to reach it.
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

        // Sign while still enabled, then disable it -- a revoked-after-signing scenario.
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

        // A fresh scope (and therefore a fresh DbContext, with no stale tracked entity left over
        // from signing above) is essential here -- reusing signScope's own ITsigService would
        // see its DbContext's own already-tracked, now-stale copy of the key instead of
        // re-querying the database, masking the disable that just happened through a separate
        // scope entirely.
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
            DnsName.Parse("hmac-sha1"), // Deliberately not the only algorithm this project implements.
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
            new byte[16], // HMAC-SHA256 always produces 32 bytes -- this is deliberately wrong.
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

        // RFC 8945 §5.3 binds a response's own MAC to the specific request that produced it --
        // verifying against a *different* request's MAC (as if this response had been spliced
        // onto an unrelated exchange) must fail exactly the same way a tampered message body would.
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

        // A genuinely broken chain -- verifying against the wrong prior MAC, exactly what a
        // dropped, reordered, or spliced-in message within a real multi-message transfer would
        // look like -- must be rejected, not silently accepted. This is the direct proof that
        // adding the lighter, RFC 8945 §5.3.1 chained-MAC mode did not also quietly weaken
        // verification: a chain that does not actually connect is still caught.
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

        // Mirrors exactly how ZoneTransferService signs, and ZoneTransferPullOverStream verifies,
        // a real multi-message AXFR/IXFR response sequence: message 1 chains from the original
        // request's own MAC, and every later message chains from whichever message before it most
        // recently carried a TSIG.
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

        // Skipping straight from message 1's own MAC to verify message 3 (as though message 2
        // never happened) must fail -- each hop in the chain is genuinely load-bearing, not
        // cosmetic.
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

        // Message 1: an ordinary, fully-signed first message in the sequence.
        byte[] requestMac = new byte[32];
        Random.Shared.NextBytes(requestMac);
        DnsMessage signed1 = await tsigService.SignResponseAsync(
            BuildMessage(13),
            key.Name,
            13,
            requestMac
        );
        (_, _, DnsMessage reparsed1) = Reparse(signed1);

        // Message 2: per RFC 8945 §5.4, a real peer may choose to leave this one entirely
        // unsigned -- up to 99 such messages are tolerated, as long as the sequence's own first
        // and last message both carry a valid TSIG. This message carries no Tsig record at all.
        byte[] unsignedMessage2Bytes = DnsMessageWriter.Write(BuildMessage(13));

        // Message 3: the next message that *does* carry a TSIG. Per RFC 8945 §5.3.1's own "DNS
        // Messages" digest component, its digest must fold in message 2's own raw bytes between
        // the running prior MAC (message 1's own) and message 3's own bytes -- computed here by
        // hand, independently of TsigService itself, to prove that class's formula genuinely
        // matches the RFC rather than merely being internally self-consistent.
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

        // Confirming the unsigned message's own bytes are genuinely load-bearing in the digest --
        // verifying as though message 2 had never existed at all (an empty "DNS Messages" gap)
        // must fail, proving this is real chaining, not a parameter this project's own code
        // silently ignores.
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
