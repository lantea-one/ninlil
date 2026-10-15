using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Authoritative.Tests.Resolution;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Tsig;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Dnssec;

/// <summary>
///     This class checks the DS RRset at a delegation point: a DS record an operator stores for
///     a delegated child is served and signed (also by an unsigned parent), it is merged with the
///     DS generated for a child that is hosted and signed here, the RRSIG over a generated DS has
///     the DS TTL as Original TTL (RFC 4034 §3.1.4), and a zone transfer sends each record once
///     (RFC 2181 §5). Each test builds one or two SQLite <see cref="AuthoritativeTestZoneHost" />
///     instances with real signed zones and reads answers through the resolver and transfers
///     through <see cref="IZoneTransferService" />, as the other signing tests do.
/// </summary>
public sealed class ZoneSigningServiceExternalDsTests
{
    /// <summary>
    ///     The apex of the parent zone most tests build.
    /// </summary>
    private const string ParentName = "par.test";

    /// <summary>
    ///     The TTL of the DS record stored for the external child.
    /// </summary>
    private const uint StoredDsTtl = 7200;

    /// <summary>
    ///     This method builds a DS record for an external child whose key lives at another
    ///     provider. The digest is a fixed pattern, since no validator checks it here.
    /// </summary>
    private static TestRecord StoredDs(string owner, ushort keyTag, uint ttl = StoredDsTtl) =>
        new(
            owner,
            DnsResourceRecordTypeEnum.DS,
            new DsRecordData(keyTag, DnssecAlgorithmEnum.Ed25519, 2, new byte[32]),
            ttl
        );

    /// <summary>
    ///     This method builds and starts the parent zone <c>par.test</c>: an external delegation
    ///     <c>ext</c> with a stored DS (TTL 7200), a hosted and signed child <c>loc</c> that also
    ///     has a stored DS for an external key, <c>dup</c> with the same stored DS entered twice,
    ///     and, when <paramref name="withInsecureDelegation" /> is set, <c>ins</c> without any DS.
    /// </summary>
    private static async Task<(AuthoritativeTestZoneHost Host, Guid ZoneId)> CreateParentAsync(
        TestZoneSigning signing,
        bool withInsecureDelegation = true,
        string transferKeyName = null
    )
    {
        AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        List<TestRecord> records =
        [
            TestRecord.Ns($"ext.{ParentName}", "ns.provider.example."),
            StoredDs($"ext.{ParentName}", 1111),
            TestRecord.Ns($"loc.{ParentName}", "ns.provider.example."),
            StoredDs($"loc.{ParentName}", 2222),
            TestRecord.Ns($"dup.{ParentName}", "ns.provider.example."),
            StoredDs($"dup.{ParentName}", 3333),
            StoredDs($"dup.{ParentName}", 3333),
            TestRecord.A($"www.{ParentName}", "192.0.2.20"),
        ];
        if (withInsecureDelegation)
            records.Add(TestRecord.Ns($"ins.{ParentName}", "ns.provider.example."));

        Guid zoneId = await host.AddZoneAsync(ParentName, records, signing);
        await host.AddZoneAsync($"loc.{ParentName}", [], TestZoneSigning.Nsec);

        // The zone cache reads the transfer key binding at start, so the key is added before it.
        if (transferKeyName is not null)
            await AllowTransferAsync(host, ParentName, transferKeyName);

        await host.StartAsync();

        return (host, zoneId);
    }

    /// <summary>
    ///     This method returns the signed data the host's cache holds for a zone.
    /// </summary>
    private static SignedZoneData GetSigned(AuthoritativeTestZoneHost host, Guid zoneId)
    {
        SignedZoneData signed = host
            .Services.GetRequiredService<SignedZoneCache>()
            .GetSignedData(zoneId);
        Assert.NotNull(signed);

        return signed;
    }

    /// <summary>
    ///     This method decodes the RDATA of an RRSIG record.
    /// </summary>
    private static RrSigRecordData DecodeRrSig(DnsResourceRecord record) =>
        (RrSigRecordData)DnsRecordDataCodecRegistry.Decode(record.Type, record.Data);

    /// <summary>
    ///     This method decodes the RDATA of a DS record.
    /// </summary>
    private static DsRecordData DecodeDs(DnsResourceRecord record) =>
        (DsRecordData)DnsRecordDataCodecRegistry.Decode(record.Type, record.Data);

    /// <summary>
    ///     This method returns the types an NSEC or NSEC3 record lists in its bitmap.
    /// </summary>
    private static IReadOnlyList<DnsResourceRecordTypeEnum> BitmapTypes(DnsResourceRecord record) =>
        record.Type == DnsResourceRecordTypeEnum.Nsec3
            ? ((Nsec3RecordData)DnsRecordDataCodecRegistry.Decode(record.Type, record.Data)).Types
            : ((NsecRecordData)DnsRecordDataCodecRegistry.Decode(record.Type, record.Data)).Types;

    /// <summary>
    ///     This method returns the chain owner of a parent-zone name: the name itself for NSEC,
    ///     the hashed name for NSEC3 (no salt, no extra iterations, the test zone's defaults).
    /// </summary>
    private static DnsName ChainOwner(string name, bool useNsec3) =>
        useNsec3
            ? Nsec3Hasher.ToOwnerName(
                Nsec3Hasher.ComputeHash(1, DnsName.Parse(name), 0, []),
                DnsName.Parse(ParentName)
            )
            : DnsName.Parse(name);

    /// <summary>
    ///     This method returns the key that tells whether two records of a transfer are the same
    ///     record: owner, type, class and RDATA.
    /// </summary>
    private static string RecordKey(DnsResourceRecord record) =>
        $"{record.Name}|{record.Type}|{record.Class}|{Convert.ToHexString(record.Data)}";

    /// <summary>
    ///     This method picks the settings for the NSEC or the NSEC3 variant of a test.
    /// </summary>
    private static TestZoneSigning SigningFor(bool useNsec3) =>
        useNsec3 ? TestZoneSigning.Nsec3() : TestZoneSigning.Nsec;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DsQuery_ForAStoredDsAtAnExternalChild_IsAnsweredWithASignatureOfTheDsTtl(
        bool useNsec3
    )
    {
        (AuthoritativeTestZoneHost host, _) = await CreateParentAsync(SigningFor(useNsec3));
        await using (host)
        {
            DnsMessage response = await host.QueryAsync(
                $"ext.{ParentName}",
                DnsResourceRecordTypeEnum.DS,
                dnssecOk: true
            );

            Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
            Assert.True(response.IsAuthoritativeAnswer);
            DnsResourceRecord ds = Assert.Single(
                response.Answers,
                record => record.Type == DnsResourceRecordTypeEnum.DS
            );
            Assert.Equal(1111, DecodeDs(ds).KeyTag);
            Assert.Equal(StoredDsTtl, ds.TimeToLive);
            RrSigRecordData signature = DecodeRrSig(
                Assert.Single(
                    response.Answers,
                    record => record.Type == DnsResourceRecordTypeEnum.RrSig
                )
            );
            Assert.Equal(DnsResourceRecordTypeEnum.DS, signature.TypeCovered);
            Assert.Equal(DnsName.Parse(ParentName), signature.SignerName);
            Assert.Equal(StoredDsTtl, signature.OriginalTimeToLiveSeconds);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Referral_ToAnExternalChildWithAStoredDs_CarriesTheSignedDsAndNoProofOfNoDs(
        bool useNsec3
    )
    {
        (AuthoritativeTestZoneHost host, _) = await CreateParentAsync(SigningFor(useNsec3));
        await using (host)
        {
            DnsMessage response = await host.QueryAsync(
                $"www.ext.{ParentName}",
                DnsResourceRecordTypeEnum.A,
                dnssecOk: true
            );

            Assert.False(response.IsAuthoritativeAnswer);
            Assert.Contains(
                response.AuthorityRecords,
                record => record.Type == DnsResourceRecordTypeEnum.NS
            );
            Assert.Contains(
                response.AuthorityRecords,
                record => record.Type == DnsResourceRecordTypeEnum.DS
            );
            Assert.Contains(
                response.AuthorityRecords,
                record =>
                    record.Type == DnsResourceRecordTypeEnum.RrSig
                    && DecodeRrSig(record).TypeCovered == DnsResourceRecordTypeEnum.DS
            );
            Assert.DoesNotContain(
                response.AuthorityRecords,
                record =>
                    record.Type is DnsResourceRecordTypeEnum.Nsec or DnsResourceRecordTypeEnum.Nsec3
            );
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignZone_ChainRecordAtADelegationWithAStoredDs_ListsDs(bool useNsec3)
    {
        (AuthoritativeTestZoneHost host, Guid zoneId) = await CreateParentAsync(
            SigningFor(useNsec3)
        );
        await using (host)
        {
            SignedZoneData signed = GetSigned(host, zoneId);

            DnsResourceRecord atExt = signed.DenialOfExistenceRecords.Single(record =>
                record.Name == ChainOwner($"ext.{ParentName}", useNsec3)
            );
            DnsResourceRecord atIns = signed.DenialOfExistenceRecords.Single(record =>
                record.Name == ChainOwner($"ins.{ParentName}", useNsec3)
            );

            Assert.Contains(DnsResourceRecordTypeEnum.DS, BitmapTypes(atExt));
            Assert.DoesNotContain(DnsResourceRecordTypeEnum.DS, BitmapTypes(atIns));
        }
    }

    [Fact]
    public async Task SignZone_Nsec3WithAnInsecureDelegation_SetsOptOutOnEveryRecord()
    {
        (AuthoritativeTestZoneHost host, Guid zoneId) = await CreateParentAsync(
            TestZoneSigning.Nsec3()
        );
        await using (host)
        {
            SignedZoneData signed = GetSigned(host, zoneId);

            Assert.All(
                signed.DenialOfExistenceRecords,
                record =>
                    Assert.True(
                        (
                            (Nsec3RecordData)
                                DnsRecordDataCodecRegistry.Decode(record.Type, record.Data)
                        ).IsOptOut
                    )
            );
        }
    }

    [Fact]
    public async Task SignZone_Nsec3WhenEveryDelegationHasADs_LeavesOptOutClear()
    {
        (AuthoritativeTestZoneHost host, Guid zoneId) = await CreateParentAsync(
            TestZoneSigning.Nsec3(),
            withInsecureDelegation: false
        );
        await using (host)
        {
            SignedZoneData signed = GetSigned(host, zoneId);

            Assert.All(
                signed.DenialOfExistenceRecords,
                record =>
                    Assert.False(
                        (
                            (Nsec3RecordData)
                                DnsRecordDataCodecRegistry.Decode(record.Type, record.Data)
                        ).IsOptOut
                    )
            );
        }
    }

    [Fact]
    public async Task SignZone_HostedSignedChildWithAStoredDs_PublishesBothOnce()
    {
        (AuthoritativeTestZoneHost host, Guid zoneId) = await CreateParentAsync(
            TestZoneSigning.Nsec
        );
        await using (host)
        {
            SignedZoneData signed = GetSigned(host, zoneId);

            IReadOnlyList<DnsResourceRecord> ds = signed.DsRecordsByDelegationName[
                DnsName.Parse($"loc.{ParentName}")
            ];

            // One DS for the hosted child's KSK (generated) and the stored one for the external
            // key, each once.
            Assert.Equal(2, ds.Count);
            Assert.Equal(2, ds.Select(RecordKey).Distinct().Count());
            Assert.Contains(ds, record => DecodeDs(record).KeyTag == 2222);
            Assert.Contains(ds, record => DecodeDs(record).KeyTag != 2222);
            Assert.NotEmpty(
                signed.GetSignatures(
                    DnsName.Parse($"loc.{ParentName}"),
                    DnsResourceRecordTypeEnum.DS
                )
            );
        }
    }

    [Fact]
    public async Task SignZone_TheSameStoredDsEnteredTwice_IsPublishedOnce()
    {
        (AuthoritativeTestZoneHost host, Guid zoneId) = await CreateParentAsync(
            TestZoneSigning.Nsec
        );
        await using (host)
        {
            SignedZoneData signed = GetSigned(host, zoneId);

            Assert.Single(signed.DsRecordsByDelegationName[DnsName.Parse($"dup.{ParentName}")]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DsQuery_AtAnUnsignedParent_AnswersTheStoredDs(bool dnssecOk)
    {
        (AuthoritativeTestZoneHost host, _) = await CreateParentAsync(TestZoneSigning.None);
        await using (host)
        {
            DnsMessage response = await host.QueryAsync(
                $"ext.{ParentName}",
                DnsResourceRecordTypeEnum.DS,
                dnssecOk
            );

            Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
            Assert.True(response.IsAuthoritativeAnswer);
            DnsResourceRecord ds = Assert.Single(response.Answers);
            Assert.Equal(DnsResourceRecordTypeEnum.DS, ds.Type);
            Assert.Equal(1111, DecodeDs(ds).KeyTag);
        }
    }

    [Fact]
    public async Task DsQuery_AtAnUnsignedParentWithoutAStoredDs_IsNoData()
    {
        (AuthoritativeTestZoneHost host, _) = await CreateParentAsync(TestZoneSigning.None);
        await using (host)
        {
            DnsMessage response = await host.QueryAsync(
                $"ins.{ParentName}",
                DnsResourceRecordTypeEnum.DS
            );

            Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
            Assert.Empty(response.Answers);
        }
    }

    [Fact]
    public async Task SignZone_GeneratedDsForAHostedChild_HasASignatureOfTheDsTtl()
    {
        // The parent's SOA TTL (1800) differs from the generated DS TTL (3600), so a signature
        // that takes its Original TTL from the SOA would not match the DS.
        AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        await using (host)
        {
            await host.AddZoneAsync(
                "ttl.test",
                [
                    new TestRecord(
                        "ttl.test",
                        DnsResourceRecordTypeEnum.Soa,
                        new SoaRecordData(
                            DnsName.Parse("ns1.ttl.test"),
                            DnsName.Parse("admin.ttl.test"),
                            1,
                            3600,
                            900,
                            604800,
                            300
                        ),
                        1800
                    ),
                    TestRecord.Ns("kid.ttl.test", "ns.kid.ttl.test"),
                ],
                TestZoneSigning.Nsec
            );
            await host.AddZoneAsync("kid.ttl.test", [], TestZoneSigning.Nsec);
            await host.StartAsync();

            DnsMessage response = await host.QueryAsync(
                "kid.ttl.test",
                DnsResourceRecordTypeEnum.DS,
                dnssecOk: true
            );

            DnsResourceRecord ds = Assert.Single(
                response.Answers,
                record => record.Type == DnsResourceRecordTypeEnum.DS
            );
            RrSigRecordData signature = DecodeRrSig(
                Assert.Single(
                    response.Answers,
                    record => record.Type == DnsResourceRecordTypeEnum.RrSig
                )
            );
            Assert.Equal(ds.TimeToLive, signature.OriginalTimeToLiveSeconds);
        }
    }

    [Fact]
    public async Task Axfr_OfASignedParentWithAStoredDs_SendsEveryRecordOnce()
    {
        string keyName = $"external-ds-key-{Guid.NewGuid()}";
        (AuthoritativeTestZoneHost host, _) = await CreateParentAsync(
            TestZoneSigning.Nsec,
            transferKeyName: keyName
        );
        await using (host)
        {
            List<DnsResourceRecord> records = await PullAxfrAsync(host, ParentName, keyName);

            Assert.Equal(records.Count, records.Select(RecordKey).Distinct().Count());
            // The stored DS of the external child is sent once, with its signature.
            Assert.Single(
                records,
                record =>
                    record.Type == DnsResourceRecordTypeEnum.DS
                    && record.Name == DnsName.Parse($"ext.{ParentName}")
            );
        }
    }

    [Fact]
    public async Task Axfr_OfASignedSecondary_SendsEveryRecordOnce()
    {
        await RunOnSignedSecondaryAsync(
            async (primary, secondary, zoneName, keys, fromPrimary) =>
            {
                List<DnsResourceRecord> fromSecondary = await PullAxfrAsync(
                    secondary,
                    zoneName,
                    keys.Secondary
                );

                Assert.Equal(
                    fromSecondary.Count,
                    fromSecondary.Select(RecordKey).Distinct().Count()
                );
                Assert.Equal(
                    fromPrimary.Select(RecordKey).Distinct().Order().ToList(),
                    fromSecondary.Select(RecordKey).Distinct().Order().ToList()
                );
            }
        );
    }

    [Fact]
    public async Task Ixfr_FullAnswerOfASignedSecondary_SendsEveryRecordOnce()
    {
        await RunOnSignedSecondaryAsync(
            async (primary, secondary, zoneName, keys, fromPrimary) =>
            {
                // Client serial 0 is behind the zone; a signed zone has no journal, so the answer is
                // the full fallback, which must hold each record once like the AXFR does.
                List<DnsResourceRecord> fromSecondary = await PullTransferAsync(
                    secondary,
                    zoneName,
                    keys.Secondary,
                    DnsResourceRecordTypeEnum.Ixfr,
                    0
                );

                Assert.Equal(DnsResourceRecordTypeEnum.Soa, fromSecondary[0].Type);
                Assert.NotEqual(DnsResourceRecordTypeEnum.Soa, fromSecondary[1].Type);
                Assert.Equal(
                    fromSecondary.Count,
                    fromSecondary.Select(RecordKey).Distinct().Count()
                );
                Assert.Equal(
                    fromPrimary.Select(RecordKey).Distinct().Order().ToList(),
                    fromSecondary.Select(RecordKey).Distinct().Order().ToList()
                );
            }
        );
    }

    /// <summary>
    ///     This method builds a signed primary and a signed secondary host for one zone, copies the
    ///     primary's AXFR into the secondary (so the secondary's cache holds the transferred DNSSEC
    ///     rows as ordinary records, as after a first pull) and runs <paramref name="body" /> with
    ///     both hosts, the zone name, the two transfer key names and the primary's AXFR records.
    /// </summary>
    private static async Task RunOnSignedSecondaryAsync(
        Func<
            AuthoritativeTestZoneHost,
            AuthoritativeTestZoneHost,
            string,
            (string Primary, string Secondary),
            List<DnsResourceRecord>,
            Task
        > body
    )
    {
        const string zoneName = "dup-secondary.test";
        string primaryKey = $"primary-key-{Guid.NewGuid()}";
        string secondaryKey = $"secondary-key-{Guid.NewGuid()}";
        Guid secondaryZoneId = Guid.NewGuid();

        AuthoritativeTestZoneHost primary = await AuthoritativeTestZoneHost.CreateAsync();
        AuthoritativeTestZoneHost secondary = await AuthoritativeTestZoneHost.CreateAsync();
        await using (primary)
        await using (secondary)
        {
            await primary.AddZoneAsync(
                zoneName,
                [
                    TestRecord.A($"www.{zoneName}", "192.0.2.20"),
                    TestRecord.Ns($"ext.{zoneName}", "ns.provider.example."),
                    StoredDs($"ext.{zoneName}", 1111),
                ],
                TestZoneSigning.Nsec
            );
            await AllowTransferAsync(primary, zoneName, primaryKey);
            await primary.StartAsync();

            // The secondary row exists before the host starts so the caches know the zone; the
            // transferred rows arrive afterwards, as on a first pull.
            await using (AsyncServiceScope scope = secondary.Services.CreateAsyncScope())
            {
                await scope
                    .ServiceProvider.GetRequiredService<IZoneRepository>()
                    .AddAsync(
                        new ZoneEntity
                        {
                            Id = secondaryZoneId,
                            Name = zoneName,
                            Type = ZoneTypeEnum.Secondary,
                            IsDnssecSigned = true,
                            CreatedAtUtc = DateTimeOffset.UtcNow,
                        }
                    );
                await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            }

            await AllowTransferAsync(secondary, zoneName, secondaryKey);
            await secondary.StartAsync();

            List<DnsResourceRecord> fromPrimary = await PullAxfrAsync(
                primary,
                zoneName,
                primaryKey
            );
            await using (AsyncServiceScope scope = secondary.Services.CreateAsyncScope())
            {
                await scope
                    .ServiceProvider.GetRequiredService<IZoneUpdateService>()
                    .BootstrapZoneAsync(
                        secondaryZoneId,
                        [
                            .. fromPrimary
                                .Select(record =>
                                    RecordEntityCodec.FromWireRecord(record, secondaryZoneId)
                                )
                                .Where(record => record is not null),
                        ]
                    );
            }

            // The zone-changed event reaches the signed-zone cache through a background channel.
            SignedZoneCache cache = secondary.Services.GetRequiredService<SignedZoneCache>();
            for (
                int attempt = 0;
                attempt < 100 && cache.GetSignedData(secondaryZoneId) is null;
                attempt++
            )
                await Task.Delay(100);
            Assert.NotNull(cache.GetSignedData(secondaryZoneId));

            await body(primary, secondary, zoneName, (primaryKey, secondaryKey), fromPrimary);
        }
    }

    /// <summary>
    ///     This method adds a TSIG key to the host and binds it to the zone, so an AXFR can
    ///     authenticate (a transfer needs a key bound in
    ///     <see cref="ZoneEntity.TransferTsigKeyNames" />).
    /// </summary>
    private static async Task AllowTransferAsync(
        AuthoritativeTestZoneHost host,
        string zoneName,
        string keyName
    )
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        IZoneRepository zones = scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        ZoneEntity zone = await zones.FindByNameAsync(zoneName);
        zone.TransferTsigKeyNames = [keyName];

        IDataProtector protector = scope
            .ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Ninlil.Dns.Server.Tsig.SharedSecret");
        byte[] rawSecret = new byte[32];
        Random.Shared.NextBytes(rawSecret);

        await scope
            .ServiceProvider.GetRequiredService<ITsigKeyRepository>()
            .AddAsync(
                new TsigKeyEntity
                {
                    Id = Guid.NewGuid(),
                    Name = keyName,
                    AlgorithmName = "hmac-sha256.",
                    SecretEncrypted = protector.Protect(Convert.ToBase64String(rawSecret)),
                    IsDisabled = false,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
        await unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    ///     This method performs a TSIG-signed AXFR of a zone against the host and returns every
    ///     wire record of the transfer except its closing SOA (see
    ///     <see cref="PullTransferAsync" />).
    /// </summary>
    private static Task<List<DnsResourceRecord>> PullAxfrAsync(
        AuthoritativeTestZoneHost host,
        string zoneName,
        string keyName
    ) => PullTransferAsync(host, zoneName, keyName, DnsResourceRecordTypeEnum.Axfr, null);

    /// <summary>
    ///     This method performs a TSIG-signed AXFR or IXFR of a zone against the host. A transfer
    ///     ends with a repeat of its leading SOA (RFC 5936 §2.2, RFC 1995 §4); the method checks
    ///     that and returns every record without it, so callers can compare records.
    /// </summary>
    /// <param name="host">The host to transfer from.</param>
    /// <param name="zoneName">The zone to transfer.</param>
    /// <param name="keyName">The TSIG key bound to the zone's transfers.</param>
    /// <param name="type">Axfr, or Ixfr to send the client SOA below.</param>
    /// <param name="ixfrClientSerial">The serial of the client SOA an IXFR carries.</param>
    private static async Task<List<DnsResourceRecord>> PullTransferAsync(
        AuthoritativeTestZoneHost host,
        string zoneName,
        string keyName,
        DnsResourceRecordTypeEnum type,
        uint? ixfrClientSerial
    )
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();
        IZoneTransferService transferService =
            scope.ServiceProvider.GetRequiredService<IZoneTransferService>();

        DnsMessage request = new()
        {
            Id = 1,
            Opcode = DnsOpcodeEnum.Query,
            Questions = { new DnsQuestion(DnsName.Parse(zoneName), type, DnsClassEnum.Internet) },
        };

        if (ixfrClientSerial is { } clientSerial)
            request.AuthorityRecords.Add(
                new DnsResourceRecord(
                    DnsName.Parse(zoneName),
                    DnsResourceRecordTypeEnum.Soa,
                    DnsClassEnum.Internet,
                    3600,
                    DnsRecordDataCodecRegistry.Encode(
                        DnsResourceRecordTypeEnum.Soa,
                        new SoaRecordData(DnsName.Root, DnsName.Root, clientSerial, 0, 0, 0, 0)
                    )
                )
            );

        DnsMessage signed = await tsigService.SignAsync(request, keyName, request.Id);
        byte[] rawMessage = DnsMessageWriter.Write(signed);
        DnsMessage reparsed = DnsMessageReader.Read(rawMessage, out int tsigOffset);

        DnsQueryContext context = new(
            reparsed,
            DnsTransportProtocolEnum.Tcp,
            IPAddress.Loopback,
            DateTimeOffset.UtcNow,
            rawMessage,
            tsigOffset
        );

        List<DnsResourceRecord> records = [];

        await foreach (
            DnsMessage message in type == DnsResourceRecordTypeEnum.Ixfr
                ? transferService.CreateIxfrResponseAsync(context)
                : transferService.CreateAxfrResponseAsync(context)
        )
        {
            Assert.Equal(DnsResponseCodeEnum.NoError, message.ResponseCode);
            records.AddRange(message.Answers);
        }

        Assert.True(records.Count > 1);
        Assert.Equal(DnsResourceRecordTypeEnum.Soa, records[^1].Type);
        Assert.Equal(records[0].Data, records[^1].Data);
        records.RemoveAt(records.Count - 1);

        return records;
    }
}
