using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Tsig.Options;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig.Tests;

/// <summary>
///     This class checks <see cref="TsigCodec" />, <see cref="TsigAlgorithm" /> and
///     <see cref="TsigService" /> against known-answer vectors made by dnspython and BIND
///     (<c>Vectors/tsig-vectors.json</c>, see the README next to it). The tests only read the
///     vectors. The service runs on an in-memory key repository and a manual clock set to each
///     vector's Time Signed, so no test depends on the wall clock or a database.
///     <para>
///         The sign tests first build each message with Ninlil's own types and require the unsigned
///         bytes to equal the vector's; a mismatch there means the writer differs from the other
///         implementation, not the MAC code.
///     </para>
/// </summary>
public class TsigKnownAnswerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Dictionary<string, Vector> Vectors = LoadVectors();

    private static readonly DnsName Example = DnsName.Parse("example.");

    /// <summary>
    ///     This class is one entry of <c>tsig-vectors.json</c>.
    /// </summary>
    public sealed class Vector
    {
        public string Id { get; set; }
        public string Source { get; set; }
        public string Kind { get; set; }
        public string KeyName { get; set; }
        public string Algorithm { get; set; }
        public string SecretBase64 { get; set; }
        public ulong TimeSigned { get; set; }
        public ushort Fudge { get; set; }
        public ushort OriginalId { get; set; }
        public string UnsignedHex { get; set; }
        public string WireHex { get; set; }
        public string MacHex { get; set; }
        public string RequestMacHex { get; set; }
        public string PriorMacHex { get; set; }
        public List<string> UnsignedMessagesHex { get; set; }

        public byte[] Secret => Convert.FromBase64String(SecretBase64);
        public byte[] Wire => Convert.FromHexString(WireHex);
    }

    private static Dictionary<string, Vector> LoadVectors()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Vectors", "tsig-vectors.json");
        List<Vector> list = JsonSerializer.Deserialize<List<Vector>>(
            File.ReadAllText(path),
            JsonOptions
        );

        return list.ToDictionary(vector => vector.Id);
    }

    /// <summary>
    ///     This method lists the ids of every vector that carries a TSIG record.
    /// </summary>
    public static IEnumerable<object[]> SignedVectorIds() =>
        Vectors
            .Values.Where(vector => vector.Kind != "unsigned")
            .Select(v => new object[] { v.Id });

    private sealed class FixedOptionsMonitor(TsigServiceOptions value)
        : IOptionsMonitor<TsigServiceOptions>
    {
        public TsigServiceOptions CurrentValue => value;

        public TsigServiceOptions Get(string name) => value;

        public IDisposable OnChange(Action<TsigServiceOptions, string> listener) => null;
    }

    /// <summary>
    ///     This class bundles a <see cref="TsigService" /> with the pieces a test needs to inspect.
    /// </summary>
    private sealed class Harness
    {
        public List<TsigKeyEntity> Keys { get; } = [];
        public InMemoryTsigKeyRepository Repository { get; }
        public ManualTimeProvider Clock { get; }
        public TsigService Service { get; }
        public TsigSecretProtector Protector { get; }

        public Harness(DateTimeOffset now)
        {
            Repository = new InMemoryTsigKeyRepository(Keys);
            Clock = new ManualTimeProvider(now);
            Protector = new TsigSecretProtector(new EphemeralDataProtectionProvider());
            Service = new TsigService(
                Repository,
                Protector,
                new FixedOptionsMonitor(new TsigServiceOptions()),
                new TsigKeyLookupCache(Clock),
                Clock
            );
        }

        public TsigKeyEntity AddKey(
            string name,
            string algorithm,
            byte[] secret,
            Action<TsigKeyEntity> configure = null
        )
        {
            TsigKeyEntity key = new()
            {
                Id = Guid.NewGuid(),
                Name = name,
                AlgorithmName = algorithm,
                SecretEncrypted = Protector.Protect(secret),
            };
            configure?.Invoke(key);
            Keys.Add(key);

            return key;
        }
    }

    private static DateTimeOffset At(ulong unixSeconds) =>
        DateTimeOffset.FromUnixTimeSeconds((long)unixSeconds);

    /// <summary>
    ///     This method builds a harness for a vector with its key stored. The key name is stored
    ///     as the message spells it (mixed case kept) or, with <paramref name="lowercase" />, as the
    ///     key API stores it. The clock starts at the vector's Time Signed.
    /// </summary>
    private static Harness CreateHarness(Vector vector, bool lowercase = false)
    {
        Harness harness = new(At(vector.TimeSigned));

        // BIND writes the key name in lowercase on the wire even when given a mixed-case one, so
        // the exact-text row is stored as the message spells it.
        DnsMessage message = DnsMessageReader.Read(vector.Wire, out _);
        string name = message.Tsig.KeyName.ToString().TrimEnd('.');
        harness.AddKey(
            lowercase ? name.ToLowerInvariant() : name,
            lowercase ? vector.Algorithm + "." : vector.Algorithm,
            vector.Secret
        );

        return harness;
    }

    private static TsigDigestKindEnum DigestKind(Vector vector) =>
        vector.Kind switch
        {
            "request" => TsigDigestKindEnum.Request,
            "first-response" or "badtime-response" => TsigDigestKindEnum.FirstResponse,
            _ => TsigDigestKindEnum.Continuation,
        };

    private static byte[] ChainMac(Vector vector) =>
        vector.RequestMacHex is not null ? Convert.FromHexString(vector.RequestMacHex)
        : vector.PriorMacHex is not null ? Convert.FromHexString(vector.PriorMacHex)
        : null;

    private static List<byte[]> UnsignedMessages(Vector vector) =>
        (vector.UnsignedMessagesHex ?? []).Select(Convert.FromHexString).ToList();

    [Theory]
    [MemberData(nameof(SignedVectorIds))]
    public void Codec_DigestOfEveryVector_EqualsTheRecordedMac(string id)
    {
        Vector vector = Vectors[id];
        byte[] wire = vector.Wire;
        DnsMessage message = DnsMessageReader.Read(wire, out int tsigOffset);

        byte[] messageBytes = TsigCodec.ExtractSignedMessageBytes(
            wire,
            tsigOffset,
            message.Tsig.OriginalId
        );
        Assert.Equal(Convert.FromHexString(vector.UnsignedHex), messageBytes);

        byte[] digestInput = TsigCodec.BuildDigestInput(
            DigestKind(vector),
            messageBytes,
            message.Tsig,
            ChainMac(vector),
            UnsignedMessages(vector)
        );
        byte[] mac = TsigAlgorithm.Find(vector.Algorithm).ComputeMac(vector.Secret, digestInput);

        Assert.Equal(vector.MacHex, Convert.ToHexStringLower(mac));
        Assert.Equal(vector.MacHex, Convert.ToHexStringLower(message.Tsig.Mac));
    }

    [Theory]
    [MemberData(nameof(SignedVectorIds))]
    public async Task Verify_EveryVector_Succeeds(string id)
    {
        Vector vector = Vectors[id];

        foreach (bool lowercase in new[] { false, true })
        {
            Harness harness = CreateHarness(vector, lowercase);
            TsigVerificationResult result = await VerifyAsync(harness, vector);

            Assert.True(result.IsValid, $"{id} (lowercase key: {lowercase}): {result.Error}");
        }
    }

    private static async Task<TsigVerificationResult> VerifyAsync(Harness harness, Vector vector)
    {
        byte[] wire = vector.Wire;
        DnsMessage message = DnsMessageReader.Read(wire, out int tsigOffset);

        return vector.Kind switch
        {
            "request" => await harness.Service.VerifyAsync(wire, tsigOffset, message),
            "continuation" => await harness.Service.VerifyContinuationAsync(
                wire,
                tsigOffset,
                message,
                ChainMac(vector),
                UnsignedMessages(vector)
            ),
            _ => await harness.Service.VerifyResponseAsync(
                wire,
                tsigOffset,
                message,
                ChainMac(vector)
            ),
        };
    }

    [Fact]
    public async Task Verify_V3WithARewrittenHeaderId_RestoresTheOriginalId()
    {
        Vector vector = Vectors["V3"];
        DnsMessage message = DnsMessageReader.Read(vector.Wire, out _);

        Assert.Equal((ushort)0xBEEF, message.Id);
        Assert.Equal((ushort)0x1234, message.Tsig.OriginalId);
        Assert.True((await VerifyAsync(CreateHarness(vector), vector)).IsValid);
    }

    private static DnsMessage SoaQuery(ushort id) =>
        new()
        {
            Id = id,
            Questions =
            {
                new DnsQuestion(Example, DnsResourceRecordTypeEnum.Soa, DnsClassEnum.Internet),
            },
        };

    private static DnsResourceRecord ExampleSoa() =>
        new(
            Example,
            DnsResourceRecordTypeEnum.Soa,
            DnsClassEnum.Internet,
            3600,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Soa,
                new SoaRecordData(
                    DnsName.Parse("ns.example.net."),
                    DnsName.Parse("hostmaster.example.org."),
                    1,
                    3600,
                    600,
                    86400,
                    300
                )
            )
        );

    private static DnsMessage SoaAnswer(ushort id)
    {
        DnsMessage answer = SoaQuery(id);
        answer.IsResponse = true;
        answer.IsAuthoritativeAnswer = true;
        answer.Answers.Add(ExampleSoa());

        return answer;
    }

    private static DnsMessage UpdateRequest(ushort id)
    {
        DnsMessage update = SoaQuery(id);
        update.Opcode = DnsOpcodeEnum.Update;
        update.AuthorityRecords.Add(
            new DnsResourceRecord(
                DnsName.Parse("www.example."),
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet,
                300,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.A,
                    new ARecordData(IPAddress.Parse("192.0.2.1"))
                )
            )
        );

        return update;
    }

    private static DnsMessage UpdateReply(ushort id)
    {
        DnsMessage reply = SoaQuery(id);
        reply.Opcode = DnsOpcodeEnum.Update;
        reply.IsResponse = true;

        return reply;
    }

    private static DnsMessage BuildUnsigned(string id) =>
        id switch
        {
            "V1" or "V2" => SoaQuery(0x1234),
            "V4" => SoaAnswer(0x1234),
            "V6" => UpdateRequest(0x3456),
            "V6-reply" => UpdateReply(0x3456),
            "A1" or "A2" or "A3" => SoaQuery(0x4567),
            "A1-reply" or "A2-reply" or "A3-reply" => SoaAnswer(0x4567),
            _ => throw new ArgumentOutOfRangeException(nameof(id)),
        };

    [Theory]
    [InlineData("V1")]
    [InlineData("V2")]
    [InlineData("V4")]
    [InlineData("V6")]
    [InlineData("V6-reply")]
    [InlineData("A1")]
    [InlineData("A1-reply")]
    [InlineData("A2")]
    [InlineData("A2-reply")]
    [InlineData("A3")]
    [InlineData("A3-reply")]
    public async Task Sign_ReproducesTheVectorByteForByte(string id)
    {
        Vector vector = Vectors[id];
        Harness harness = CreateHarness(vector);
        DnsMessage message = BuildUnsigned(id);

        // Precondition: Ninlil serializes the same unsigned message as the other implementation.
        Assert.Equal(vector.UnsignedHex, Convert.ToHexStringLower(DnsMessageWriter.Write(message)));

        DnsMessage signed =
            vector.Kind == "request"
                ? await harness.Service.SignAsync(
                    message,
                    vector.KeyName.TrimEnd('.'),
                    vector.OriginalId
                )
                : await harness.Service.SignResponseAsync(
                    message,
                    vector.KeyName.TrimEnd('.'),
                    vector.OriginalId,
                    ChainMac(vector)
                );

        Assert.Equal(vector.WireHex, Convert.ToHexStringLower(DnsMessageWriter.Write(signed)));
    }

    [Fact]
    public async Task SignErrorResponse_ForBadTime_ReproducesV7()
    {
        Vector vector = Vectors["V7"];
        Vector request = Vectors["V1"];
        Harness harness = CreateHarness(request);

        // The server clock is 600 seconds ahead of the request; the reply repeats the request's
        // Time Signed and carries the server time in Other Data.
        harness.Clock.Set(At(request.TimeSigned + 600));

        DnsMessage requestMessage = DnsMessageReader.Read(request.Wire, out _);
        DnsMessage response = SoaQuery(0x1234);
        response.IsResponse = true;
        response.ResponseCode = DnsResponseCodeEnum.NotAuthoritative;
        Assert.Equal(
            vector.UnsignedHex,
            Convert.ToHexStringLower(DnsMessageWriter.Write(response))
        );

        TsigVerificationResult failure = TsigVerificationResult.Failure(
            DnsResponseCodeEnum.BadTime,
            harness.Keys[0]
        );
        DnsMessage signed = await harness.Service.SignErrorResponseAsync(
            response,
            requestMessage.Tsig,
            failure
        );

        Assert.Equal(vector.WireHex, Convert.ToHexStringLower(DnsMessageWriter.Write(signed)));
    }

    /// <summary>
    ///     This method replaces V1's TSIG record with one carrying a MAC of the given length and
    ///     returns the bytes as a transport would deliver them.
    /// </summary>
    private static (byte[] Wire, int Offset, DnsMessage Message) WithMacOfLength(
        Vector vector,
        int length
    )
    {
        DnsMessage message = DnsMessageReader.Read(vector.Wire, out _);
        DnsTsigOptions tsig = message.Tsig;
        message.Tsig = new DnsTsigOptions(
            tsig.KeyName,
            tsig.AlgorithmName,
            tsig.TimeSigned,
            tsig.Fudge,
            tsig.Mac.Concat(new byte[length]).Take(length).ToArray(),
            tsig.OriginalId,
            tsig.Error,
            tsig.OtherData
        );
        byte[] wire = DnsMessageWriter.Write(message);
        DnsMessage reparsed = DnsMessageReader.Read(wire, out int offset);

        return (wire, offset, reparsed);
    }

    [Theory]
    [InlineData("V1", 33, DnsResponseCodeEnum.FormatError)]
    [InlineData("V1", 15, DnsResponseCodeEnum.FormatError)]
    [InlineData("V1", 20, DnsResponseCodeEnum.BadTruncation)]
    [InlineData("V1", 16, DnsResponseCodeEnum.BadTruncation)]
    [InlineData("A1", 21, DnsResponseCodeEnum.FormatError)]
    [InlineData("A1", 19, DnsResponseCodeEnum.BadTruncation)]
    [InlineData("A3", 65, DnsResponseCodeEnum.FormatError)]
    [InlineData("A3", 63, DnsResponseCodeEnum.BadTruncation)]
    public async Task Verify_AMacOfTheWrongLength_IsFormErrOrBadTrunc(
        string id,
        int length,
        DnsResponseCodeEnum expected
    )
    {
        Vector vector = Vectors[id];
        Harness harness = CreateHarness(vector);
        (byte[] wire, int offset, DnsMessage message) = WithMacOfLength(vector, length);

        TsigVerificationResult result = await harness.Service.VerifyAsync(wire, offset, message);

        Assert.False(result.IsValid);
        Assert.Equal(expected, result.Error);
    }

    [Fact]
    public async Task Verify_ATamperedMacWithAWrongClock_IsBadSigNotBadTime()
    {
        Vector vector = Vectors["V1"];
        Harness harness = CreateHarness(vector);
        harness.Clock.Advance(TimeSpan.FromHours(1));
        byte[] wire = vector.Wire;

        // The MAC ends 6 octets before the end: Original ID, Error and Other Len follow it.
        wire[^7] ^= 0xFF;
        DnsMessage message = DnsMessageReader.Read(wire, out int offset);

        TsigVerificationResult result = await harness.Service.VerifyAsync(wire, offset, message);

        Assert.Equal(DnsResponseCodeEnum.BadVersionOrBadSignature, result.Error);
    }

    [Fact]
    public async Task Verify_AnIntactMessageWithAWrongClock_IsBadTimeAndCarriesTheKey()
    {
        Vector vector = Vectors["V1"];
        Harness harness = CreateHarness(vector);
        harness.Clock.Advance(TimeSpan.FromHours(1));

        TsigVerificationResult result = await VerifyAsync(harness, vector);

        Assert.Equal(DnsResponseCodeEnum.BadTime, result.Error);
        Assert.Same(harness.Keys[0], result.Key);
    }

    [Fact]
    public async Task Verify_ATimeJustInsideTheFudge_Succeeds()
    {
        Vector vector = Vectors["V1"];
        Harness harness = CreateHarness(vector);
        harness.Clock.Advance(TimeSpan.FromSeconds(vector.Fudge));

        Assert.True((await VerifyAsync(harness, vector)).IsValid);
    }

    [Fact]
    public async Task Verify_AKeyOfAnotherAlgorithm_IsBadKey()
    {
        Vector vector = Vectors["V1"];
        Harness harness = new(At(vector.TimeSigned));
        harness.AddKey("transfer-key", "hmac-sha512", new byte[64]);

        TsigVerificationResult result = await VerifyAsync(harness, vector);

        Assert.Equal(DnsResponseCodeEnum.BadKey, result.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VerifyAndSign_WithAClusterKey_AreRefused(bool isInvite)
    {
        Vector vector = Vectors["V1"];
        Harness harness = new(At(vector.TimeSigned));
        harness.AddKey(
            "transfer-key",
            "hmac-sha256",
            vector.Secret,
            key =>
            {
                if (isInvite)
                    key.ClusterInviteExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1);
                else
                    key.ClusterNodeId = Guid.NewGuid();
            }
        );

        TsigVerificationResult result = await VerifyAsync(harness, vector);

        Assert.Equal(DnsResponseCodeEnum.BadKey, result.Error);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.SignAsync(SoaQuery(1), "transfer-key", 1)
        );
    }

    [Fact]
    public async Task SignErrorResponse_ForBadKeyAndBadSig_IsAnUnsignedNotAuthReply()
    {
        Vector vector = Vectors["V2"];
        Harness harness = CreateHarness(vector);
        DnsMessage request = DnsMessageReader.Read(vector.Wire, out _);

        foreach (
            DnsResponseCodeEnum error in new[]
            {
                DnsResponseCodeEnum.BadKey,
                DnsResponseCodeEnum.BadVersionOrBadSignature,
                DnsResponseCodeEnum.BadTruncation,
            }
        )
        {
            DnsMessage response = SoaQuery(0x1234);
            response.IsResponse = true;

            DnsMessage result = await harness.Service.SignErrorResponseAsync(
                response,
                request.Tsig,
                TsigVerificationResult.Failure(error)
            );

            Assert.Equal(DnsResponseCodeEnum.NotAuthoritative, result.ResponseCode);
            Assert.NotNull(result.Tsig);
            Assert.Empty(result.Tsig.Mac);
            Assert.Equal(error, result.Tsig.Error);
            Assert.Empty(result.Tsig.OtherData);
            Assert.Equal(request.Tsig.KeyName, result.Tsig.KeyName);
            Assert.Equal(request.Tsig.AlgorithmName, result.Tsig.AlgorithmName);
            Assert.Equal(request.Tsig.Fudge, result.Tsig.Fudge);
            Assert.Equal(response.Id, result.Tsig.OriginalId);
            Assert.Equal(
                (ulong)harness.Clock.GetUtcNow().ToUnixTimeSeconds(),
                result.Tsig.TimeSigned
            );

            // The extended code survives the wire; the header carries NOTAUTH.
            DnsMessage reparsed = DnsMessageReader.Read(DnsMessageWriter.Write(result), out _);
            Assert.Equal(DnsResponseCodeEnum.NotAuthoritative, reparsed.ResponseCode);
            Assert.Equal(error, reparsed.Tsig.Error);
        }
    }

    [Fact]
    public async Task SignErrorResponse_ForFormErr_HasNoTsig()
    {
        Vector vector = Vectors["V1"];
        Harness harness = CreateHarness(vector);
        DnsMessage request = DnsMessageReader.Read(vector.Wire, out _);
        DnsMessage response = SoaQuery(0x1234);
        response.IsResponse = true;

        DnsMessage result = await harness.Service.SignErrorResponseAsync(
            response,
            request.Tsig,
            TsigVerificationResult.Failure(DnsResponseCodeEnum.FormatError)
        );

        Assert.Equal(DnsResponseCodeEnum.FormatError, result.ResponseCode);
        Assert.Null(result.Tsig);
    }

    [Fact]
    public async Task KeyLookup_ADisabledKeyKeepsVerifyingUntilTheCacheLifetimeEnds()
    {
        Vector vector = Vectors["V1"];
        Harness harness = CreateHarness(vector);

        Assert.True((await VerifyAsync(harness, vector)).IsValid);
        Assert.Equal(1, harness.Repository.LookupCount);

        // Replace the stored row by a disabled copy, as a database update would.
        TsigKeyEntity enabled = harness.Keys[0];
        harness.Keys[0] = new TsigKeyEntity
        {
            Id = enabled.Id,
            Name = enabled.Name,
            AlgorithmName = enabled.AlgorithmName,
            SecretEncrypted = enabled.SecretEncrypted,
            IsDisabled = true,
        };

        Assert.True((await VerifyAsync(harness, vector)).IsValid);
        Assert.Equal(1, harness.Repository.LookupCount);

        harness.Clock.Advance(TsigKeyLookupCache.Lifetime + TimeSpan.FromSeconds(1));

        Assert.Equal(DnsResponseCodeEnum.BadKey, (await VerifyAsync(harness, vector)).Error);
        Assert.Equal(2, harness.Repository.LookupCount);
    }

    [Fact]
    public async Task KeyLookup_AnUnknownNameIsLookedUpOncePerLifetime()
    {
        Vector vector = Vectors["V1"];
        Harness harness = new(At(vector.TimeSigned));

        for (int i = 0; i < 3; i++)
            Assert.Equal(DnsResponseCodeEnum.BadKey, (await VerifyAsync(harness, vector)).Error);

        Assert.Equal(1, harness.Repository.LookupCount);

        harness.Clock.Advance(TsigKeyLookupCache.Lifetime + TimeSpan.FromSeconds(1));
        await VerifyAsync(harness, vector);

        Assert.Equal(2, harness.Repository.LookupCount);
    }

    [Fact]
    public async Task KeyLookup_AMixedCaseNameTriesTheLowercaseNameThenTheExactText()
    {
        Harness harness = new(DateTimeOffset.UtcNow);
        TsigKeyEntity key = harness.AddKey("Legacy.Key", "hmac-sha256", new byte[32]);
        TsigKeyLookupCache cache = new(harness.Clock);

        TsigKeyEntity found = await cache.FindAsync("Legacy.Key.", harness.Repository, default);

        Assert.Same(key, found);
        Assert.Equal(2, harness.Repository.LookupCount);
    }

    [Fact]
    public async Task KeyLookup_TheCacheHoldsAtMost1024EntriesAndDropsTheOldest()
    {
        Harness harness = new(DateTimeOffset.UtcNow);
        TsigKeyLookupCache cache = new(harness.Clock);

        for (int i = 0; i <= TsigKeyLookupCache.MaximumEntries; i++)
        {
            await cache.FindAsync($"k{i}", harness.Repository, default);
            harness.Clock.Advance(TimeSpan.FromMilliseconds(1));
        }

        int before = harness.Repository.LookupCount;
        await cache.FindAsync($"k{TsigKeyLookupCache.MaximumEntries}", harness.Repository, default);
        Assert.Equal(before, harness.Repository.LookupCount);

        await cache.FindAsync("k0", harness.Repository, default);
        Assert.Equal(before + 1, harness.Repository.LookupCount);
    }
}
