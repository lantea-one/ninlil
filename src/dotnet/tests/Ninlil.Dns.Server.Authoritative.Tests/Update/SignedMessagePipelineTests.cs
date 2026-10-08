using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.Extension;
using Ninlil.Dns.Server.Core.Extension;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Dnssec.Extension;
using Ninlil.Dns.Server.Tsig;
using Ninlil.Dns.Server.Tsig.Extension;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Update;

/// <summary>
///     This class tests TSIG-signed QUERY, NOTIFY and UPDATE messages through the real
///     <see cref="DnsQueryProcessingPipeline" />, the real TSIG service and the real authoritative
///     resolvers on one host with a temporary SQLite database. A request is signed with
///     <see cref="ITsigService.SignAsync" />, sent as raw bytes and the reply is verified with
///     <see cref="ITsigService.VerifyResponseAsync" /> against the request MAC. No socket is
///     involved.
/// </summary>
public sealed class SignedMessagePipelineTests : IAsyncLifetime
{
    private const string ZoneName = "signed-update.example.com";

    // Updates are enabled, but no key is listed, so no key may update it.
    private const string UnboundZoneName = "unbound-update.example.com";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-signed-pipeline-tests-{Guid.NewGuid()}.sqlite3"
    );

    private readonly OffsetTimeProvider _clock = new();

    private IHost _host;
    private Guid _zoneId;
    private string _keyName;
    private string _unlistedKeyName;

    /// <summary>
    ///     This clock reads the system time plus a settable offset, so a test can sign a request
    ///     "in the past". Timers still use the system clock, so the host's background services are
    ///     unaffected.
    /// </summary>
    private sealed class OffsetTimeProvider : TimeProvider
    {
        public TimeSpan Offset { get; set; }

        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Offset;
    }

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddSingleton<TimeProvider>(_clock);
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);
        builder.Services.AddSingleton<IDataProtectionProvider>(
            new EphemeralDataProtectionProvider()
        );
        builder.Services.AddNinlilDnsServerTsig(builder.Configuration);
        builder.Services.AddNinlilDnsServerDnssec();
        builder.Services.AddNinlilDnsServerAuthoritative(builder.Configuration);

        _host = builder.Build();

        await using AsyncServiceScope seedScope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = seedScope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        IZoneRepository zoneRepository =
            seedScope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository =
            seedScope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IUnitOfWork unitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        ITsigKeyRepository tsigKeyRepository =
            seedScope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();
        IDataProtectionProvider dataProtectionProvider =
            seedScope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();

        _zoneId = Guid.NewGuid();
        _keyName = $"pipeline-test-key-{Guid.NewGuid()}";
        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = _zoneId,
                Name = ZoneName,
                Type = ZoneTypeEnum.Primary,
                DynamicUpdateEnabled = true,
                UpdateTsigKeyNames = [_keyName.ToUpperInvariant()],
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = Guid.NewGuid(),
                Name = UnboundZoneName,
                Type = ZoneTypeEnum.Primary,
                DynamicUpdateEnabled = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        await recordRepository.AddAsync(
            new RecordEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = _zoneId,
                Name = ZoneName,
                Type = DnsResourceRecordTypeEnum.Soa,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 3600,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    typeof(SoaRecordData),
                    new SoaRecordData(
                        DnsName.Parse($"ns1.{ZoneName}"),
                        DnsName.Parse($"admin.{ZoneName}"),
                        100,
                        3600,
                        900,
                        604800,
                        3600
                    )
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );

        IDataProtector protector = dataProtectionProvider.CreateProtector(
            "Ninlil.Dns.Server.Tsig.SharedSecret"
        );
        byte[] rawSecret = new byte[32];
        Random.Shared.NextBytes(rawSecret);

        _unlistedKeyName = $"pipeline-unlisted-key-{Guid.NewGuid()}";

        foreach (string name in new[] { _keyName, _unlistedKeyName })
            await tsigKeyRepository.AddAsync(
                new TsigKeyEntity
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    AlgorithmName = "hmac-sha256",
                    SecretEncrypted = protector.Protect(Convert.ToBase64String(rawSecret)),
                    IsDisabled = false,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
        await unitOfWork.SaveChangesAsync();

        // Starts the zone cache and the pipeline's workers (the pipeline is a hosted service).
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private static DnsQuestion SoaQuestion(string name) =>
        new(DnsName.Parse(name), DnsResourceRecordTypeEnum.Soa, DnsClassEnum.Internet);

    /// <summary>
    ///     This method sends raw request bytes over a simulated UDP transport and returns the
    ///     reply bytes.
    /// </summary>
    private async Task<byte[]> SendAsync(byte[] requestBytes)
    {
        DnsQueryProcessingPipeline pipeline =
            _host.Services.GetRequiredService<DnsQueryProcessingPipeline>();
        TaskCompletionSource<byte[]> reply = new();

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                requestBytes,
                DnsTransportProtocolEnum.Udp,
                IPAddress.Loopback,
                (bytes, _) =>
                {
                    reply.TrySetResult(bytes.ToArray());
                    return ValueTask.CompletedTask;
                }
            )
        );

        return await reply.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private async Task<DnsMessage> SignAsync(DnsMessage request, string keyName = null)
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();

        return await tsigService.SignAsync(request, keyName ?? _keyName, request.Id);
    }

    /// <summary>
    ///     This method checks that the reply's TSIG record verifies as an answer to the request.
    /// </summary>
    private async Task AssertSignedReplyAsync(DnsMessage signedRequest, byte[] replyBytes)
    {
        DnsMessage reply = DnsMessageReader.Read(replyBytes, out int tsigOffset);
        Assert.NotNull(reply.Tsig);

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();
        TsigVerificationResult result = await tsigService.VerifyResponseAsync(
            replyBytes,
            tsigOffset,
            reply,
            signedRequest.Tsig.Mac,
            reply.Tsig.KeyName.ToString()
        );

        Assert.True(result.IsValid, $"The reply's TSIG failed with {result.Error}.");
    }

    private static DnsMessage BuildUpdate(
        string hostName,
        byte lastOctet,
        string zoneName = ZoneName
    ) =>
        new()
        {
            Id = 501,
            Opcode = DnsOpcodeEnum.Update,
            Questions = { SoaQuestion(zoneName) },
            AuthorityRecords =
            {
                new DnsResourceRecord(
                    DnsName.Parse(hostName),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet,
                    300,
                    [192, 0, 2, lastOctet]
                ),
            },
        };

    [Fact]
    public async Task SignedSoaQuery_GetsAnAnswerWhoseTsigVerifiesAgainstTheRequestMac()
    {
        DnsMessage signed = await SignAsync(
            new DnsMessage
            {
                Id = 500,
                Opcode = DnsOpcodeEnum.Query,
                Questions = { SoaQuestion(ZoneName) },
            }
        );

        byte[] replyBytes = await SendAsync(DnsMessageWriter.Write(signed));

        DnsMessage reply = DnsMessageReader.Read(replyBytes);
        Assert.Equal(DnsResponseCodeEnum.NoError, reply.ResponseCode);
        Assert.Contains(reply.Answers, record => record.Type == DnsResourceRecordTypeEnum.Soa);
        await AssertSignedReplyAsync(signed, replyBytes);
    }

    [Fact]
    public async Task SignedNotifyForAZoneThisServerDoesNotHold_GetsASignedRefusedReply()
    {
        DnsMessage signed = await SignAsync(
            new DnsMessage
            {
                Id = 502,
                Opcode = DnsOpcodeEnum.Notify,
                Questions = { SoaQuestion("not-held.example.net") },
            }
        );

        byte[] replyBytes = await SendAsync(DnsMessageWriter.Write(signed));

        Assert.Equal(DnsResponseCodeEnum.Refused, DnsMessageReader.Read(replyBytes).ResponseCode);
        await AssertSignedReplyAsync(signed, replyBytes);
    }

    [Fact]
    public async Task SignedUpdateWithAValidKey_AppliesTheChangeAndGetsASignedNoErrorReply()
    {
        DnsMessage signed = await SignAsync(BuildUpdate($"added.{ZoneName}", 55));

        byte[] replyBytes = await SendAsync(DnsMessageWriter.Write(signed));

        Assert.Equal(DnsResponseCodeEnum.NoError, DnsMessageReader.Read(replyBytes).ResponseCode);
        await AssertSignedReplyAsync(signed, replyBytes);

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IReadOnlyList<RecordEntity> records = await scope
            .ServiceProvider.GetRequiredService<IRecordRepository>()
            .ListByZoneIdAsync(_zoneId);
        Assert.Contains(
            records,
            record =>
                record.Type == DnsResourceRecordTypeEnum.A
                && record.Name.StartsWith("added.", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task SignedUpdateWithATamperedBody_GetsNotAuthWithBadSigAndAnEmptyMac()
    {
        DnsMessage signed = await SignAsync(BuildUpdate($"tampered.{ZoneName}", 56));

        // Adding a record after signing makes the MAC wrong, the same as a wrong secret would.
        signed.AuthorityRecords.Add(
            new DnsResourceRecord(
                DnsName.Parse($"extra.{ZoneName}"),
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet,
                300,
                [192, 0, 2, 99]
            )
        );

        byte[] replyBytes = await SendAsync(DnsMessageWriter.Write(signed));
        DnsMessage reply = DnsMessageReader.Read(replyBytes);

        // BADSIG (16) does not fit the four-bit header field, so the header says NOTAUTH.
        Assert.Equal(DnsResponseCodeEnum.NotAuthoritative, reply.ResponseCode);
        Assert.NotNull(reply.Tsig);
        Assert.Equal(DnsResponseCodeEnum.BadVersionOrBadSignature, reply.Tsig.Error);
        Assert.Empty(reply.Tsig.Mac);

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IReadOnlyList<RecordEntity> records = await scope
            .ServiceProvider.GetRequiredService<IRecordRepository>()
            .ListByZoneIdAsync(_zoneId);
        Assert.DoesNotContain(
            records,
            record => record.Name.StartsWith("tampered.", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task SignedUpdateWithAnUnknownKey_GetsNotAuthWithBadKey()
    {
        DnsMessage signed = await SignAsync(BuildUpdate($"unknown-key.{ZoneName}", 57));
        DnsTsigOptions original = signed.Tsig;
        signed.Tsig = new DnsTsigOptions(
            DnsName.Parse("no-such-key.example."),
            original.AlgorithmName,
            original.TimeSigned,
            original.Fudge,
            original.Mac,
            original.OriginalId,
            original.Error,
            original.OtherData
        );

        DnsMessage reply = DnsMessageReader.Read(await SendAsync(DnsMessageWriter.Write(signed)));

        Assert.Equal(DnsResponseCodeEnum.NotAuthoritative, reply.ResponseCode);
        Assert.Equal(DnsResponseCodeEnum.BadKey, reply.Tsig.Error);
        Assert.Empty(reply.Tsig.Mac);
    }

    [Fact]
    public async Task SignedUpdateWithAStaleTime_GetsASignedBadTimeReply()
    {
        // Sign ten minutes in the past (the default fudge is five minutes), then send "now".
        _clock.Offset = TimeSpan.FromMinutes(-10);
        DnsMessage signed;

        try
        {
            signed = await SignAsync(BuildUpdate($"stale.{ZoneName}", 58));
        }
        finally
        {
            _clock.Offset = TimeSpan.Zero;
        }

        byte[] replyBytes = await SendAsync(DnsMessageWriter.Write(signed));
        DnsMessage reply = DnsMessageReader.Read(replyBytes);

        Assert.Equal(DnsResponseCodeEnum.NotAuthoritative, reply.ResponseCode);
        Assert.Equal(DnsResponseCodeEnum.BadTime, reply.Tsig.Error);
        // RFC 8945 §5.2.3: the reply is signed, repeats the request's Time Signed and carries
        // the server time (six octets) in Other Data.
        Assert.NotEmpty(reply.Tsig.Mac);
        Assert.Equal(signed.Tsig.TimeSigned, reply.Tsig.TimeSigned);
        Assert.Equal(6, reply.Tsig.OtherData.Length);
    }

    [Fact]
    public async Task UnsignedUpdate_IsRefusedWithoutATsigRecord()
    {
        byte[] replyBytes = await SendAsync(
            DnsMessageWriter.Write(BuildUpdate($"unsigned.{ZoneName}", 59))
        );
        DnsMessage reply = DnsMessageReader.Read(replyBytes);

        Assert.Equal(DnsResponseCodeEnum.Refused, reply.ResponseCode);
        Assert.Null(reply.Tsig);
    }

    [Fact]
    public async Task SignedUpdateWithAValidKeyNotListedForTheZone_GetsASignedRefusedAndChangesNothing()
    {
        DnsMessage signed = await SignAsync(
            BuildUpdate($"unlisted.{ZoneName}", 60),
            _unlistedKeyName
        );

        byte[] replyBytes = await SendAsync(DnsMessageWriter.Write(signed));

        Assert.Equal(DnsResponseCodeEnum.Refused, DnsMessageReader.Read(replyBytes).ResponseCode);
        await AssertSignedReplyAsync(signed, replyBytes);

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IReadOnlyList<RecordEntity> records = await scope
            .ServiceProvider.GetRequiredService<IRecordRepository>()
            .ListByZoneIdAsync(_zoneId);
        Assert.DoesNotContain(
            records,
            record => record.Name.StartsWith("unlisted.", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task SignedUpdateForAZoneWithAnEmptyKeyList_GetsASignedRefusedForEveryKey()
    {
        foreach (string key in new[] { _keyName, _unlistedKeyName })
        {
            DnsMessage signed = await SignAsync(
                BuildUpdate($"x.{UnboundZoneName}", 61, UnboundZoneName),
                key
            );

            byte[] replyBytes = await SendAsync(DnsMessageWriter.Write(signed));

            Assert.Equal(
                DnsResponseCodeEnum.Refused,
                DnsMessageReader.Read(replyBytes).ResponseCode
            );
            await AssertSignedReplyAsync(signed, replyBytes);
        }
    }
}
