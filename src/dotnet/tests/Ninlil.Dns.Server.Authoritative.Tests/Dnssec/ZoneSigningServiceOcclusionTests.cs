using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Authoritative.Tests.Resolution;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Tsig;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Dnssec;

/// <summary>
///     This class checks which data <see cref="ZoneSigningService" /> signs and chains in a zone
///     with delegations: glue and data below a zone cut get no RRSIG and no NSEC or NSEC3 record
///     (RFC 4035 §2.2, §2.3), the chain records use the negative-caching TTL (RFC 9077 §3), and a
///     scheduled re-sign signs everything again. Each test builds a SQLite
///     <see cref="AuthoritativeTestZoneHost" /> with a real signed zone, as the other signing
///     tests do, and reads the result from <see cref="SignedZoneCache" />. Every check runs for
///     an NSEC chain and for an NSEC3 chain (<c>useNsec3</c>).
/// </summary>
public sealed class ZoneSigningServiceOcclusionTests
{
    /// <summary>
    ///     The apex of the signed test zone every test builds.
    /// </summary>
    private const string ZoneName = "occ.test";

    /// <summary>
    ///     This method builds the test zone: a delegation with glue and deeper data below it, a
    ///     nested delegation under another delegation, and one ordinary host. When
    ///     <paramref name="transferKeyName" /> is set, a TSIG key of that name may transfer the
    ///     zone. The default SOA of
    ///     the host has TTL 3600 and MINIMUM 300.
    /// </summary>
    private static async Task<(AuthoritativeTestZoneHost Host, Guid ZoneId)> CreateHostAsync(
        bool useNsec3,
        string transferKeyName = null
    )
    {
        AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        Guid zoneId = await host.AddZoneAsync(
            ZoneName,
            [
                TestRecord.Ns($"sub.{ZoneName}", $"ns.sub.{ZoneName}"),
                TestRecord.A($"ns.sub.{ZoneName}", "192.0.2.53"),
                new TestRecord(
                    $"deep.x.sub.{ZoneName}",
                    DnsResourceRecordTypeEnum.Txt,
                    new TxtRecordData(["occluded"])
                ),
                TestRecord.Ns($"b.{ZoneName}", "ns.elsewhere.example."),
                TestRecord.Ns($"a.b.{ZoneName}", "ns2.elsewhere.example."),
                TestRecord.A($"www.{ZoneName}", "192.0.2.20"),
            ],
            useNsec3 ? TestZoneSigning.Nsec3() : TestZoneSigning.Nsec
        );
        // The zone cache reads the transfer key binding at start, so the key is added before it.
        if (transferKeyName is not null)
            await AllowTransferAsync(host, transferKeyName);

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
    ///     This method returns the owner a name has in the zone's chain: the name itself for NSEC,
    ///     the hashed name for NSEC3 (no salt, no extra iterations, the test zone's defaults).
    /// </summary>
    private static DnsName ChainOwner(string name, bool useNsec3) =>
        useNsec3 ? Nsec3Owner(name) : DnsName.Parse(name);

    /// <summary>
    ///     This method returns the NSEC3 owner name of <paramref name="name"/> in the test zone,
    ///     hashed with SHA-1, no salt and no extra iterations (RFC 5155 §5).
    /// </summary>
    private static DnsName Nsec3Owner(string name) =>
        Nsec3Hasher.ToOwnerName(
            Nsec3Hasher.ComputeHash(1, DnsName.Parse(name), 0, []),
            DnsName.Parse(ZoneName)
        );

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignZone_GlueAndOccludedData_GetNoSignature(bool useNsec3)
    {
        (AuthoritativeTestZoneHost host, Guid zoneId) = await CreateHostAsync(useNsec3);
        await using (host)
        {
            SignedZoneData signed = GetSigned(host, zoneId);

            Assert.Empty(
                signed.GetSignatures(
                    DnsName.Parse($"ns.sub.{ZoneName}"),
                    DnsResourceRecordTypeEnum.A
                )
            );
            Assert.Empty(
                signed.GetSignatures(
                    DnsName.Parse($"deep.x.sub.{ZoneName}"),
                    DnsResourceRecordTypeEnum.Txt
                )
            );
            // Guard: ordinary authoritative data is still signed.
            Assert.NotEmpty(
                signed.GetSignatures(DnsName.Parse($"www.{ZoneName}"), DnsResourceRecordTypeEnum.A)
            );
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignZone_NamesBelowAZoneCut_AreNotInTheChain(bool useNsec3)
    {
        (AuthoritativeTestZoneHost host, Guid zoneId) = await CreateHostAsync(useNsec3);
        await using (host)
        {
            SignedZoneData signed = GetSigned(host, zoneId);
            HashSet<DnsName> chainOwners =
            [
                .. signed.DenialOfExistenceRecords.Select(record => record.Name),
            ];

            foreach (
                string occluded in new[]
                {
                    $"ns.sub.{ZoneName}",
                    $"x.sub.{ZoneName}",
                    $"deep.x.sub.{ZoneName}",
                    $"a.b.{ZoneName}",
                }
            )
                Assert.DoesNotContain(ChainOwner(occluded, useNsec3), chainOwners);

            // Guard: the cuts themselves, the apex and the host are chained.
            foreach (
                string chained in new[]
                {
                    ZoneName,
                    $"sub.{ZoneName}",
                    $"b.{ZoneName}",
                    $"www.{ZoneName}",
                }
            )
                Assert.Contains(ChainOwner(chained, useNsec3), chainOwners);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignZone_ChainRecordAtACut_ListsNsRrsigAndNsecOnly(bool useNsec3)
    {
        (AuthoritativeTestZoneHost host, Guid zoneId) = await CreateHostAsync(useNsec3);
        await using (host)
        {
            SignedZoneData signed = GetSigned(host, zoneId);
            DnsResourceRecord record = signed.DenialOfExistenceRecords.Single(candidate =>
                candidate.Name == ChainOwner($"sub.{ZoneName}", useNsec3)
            );

            IReadOnlyList<DnsResourceRecordTypeEnum> types = useNsec3
                ? (
                    (Nsec3RecordData)DnsRecordDataCodecRegistry.Decode(record.Type, record.Data)
                ).Types
                : (
                    (NsecRecordData)DnsRecordDataCodecRegistry.Decode(record.Type, record.Data)
                ).Types;

            Assert.Contains(DnsResourceRecordTypeEnum.NS, types);
            Assert.Contains(DnsResourceRecordTypeEnum.RrSig, types);
            Assert.DoesNotContain(DnsResourceRecordTypeEnum.A, types);
            Assert.DoesNotContain(DnsResourceRecordTypeEnum.Txt, types);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Referral_WithDnssecOk_CarriesGlueWithoutSignature(bool useNsec3)
    {
        (AuthoritativeTestZoneHost host, _) = await CreateHostAsync(useNsec3);
        await using (host)
        {
            DnsMessage response = await host.QueryAsync(
                $"x.sub.{ZoneName}",
                DnsResourceRecordTypeEnum.A,
                dnssecOk: true
            );

            Assert.Contains(
                response.AdditionalRecords,
                record =>
                    record.Type == DnsResourceRecordTypeEnum.A
                    && record.Name == DnsName.Parse($"ns.sub.{ZoneName}")
            );
            Assert.DoesNotContain(
                response.AdditionalRecords,
                record => record.Type == DnsResourceRecordTypeEnum.RrSig
            );
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignedTransfer_OfAZoneWithGlue_SendsTheGlueWithoutASignature(bool useNsec3)
    {
        string keyName = $"occlusion-key-{Guid.NewGuid()}";
        (AuthoritativeTestZoneHost host, _) = await CreateHostAsync(useNsec3, keyName);
        await using (host)
        {
            List<DnsResourceRecord> records = await PullAxfrAsync(host, keyName);

            Assert.Contains(
                records,
                record =>
                    record.Type == DnsResourceRecordTypeEnum.A
                    && record.Name == DnsName.Parse($"ns.sub.{ZoneName}")
            );
            Assert.DoesNotContain(
                records,
                record =>
                    record.Type == DnsResourceRecordTypeEnum.RrSig
                    && record.Name == DnsName.Parse($"ns.sub.{ZoneName}")
            );
            Assert.DoesNotContain(
                records,
                record =>
                    record.Type == DnsResourceRecordTypeEnum.RrSig
                    && record.Name == DnsName.Parse($"deep.x.sub.{ZoneName}")
            );
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignZone_ChainRecords_UseTheNegativeCachingTtl(bool useNsec3)
    {
        (AuthoritativeTestZoneHost host, Guid zoneId) = await CreateHostAsync(useNsec3);
        await using (host)
        {
            SignedZoneData signed = GetSigned(host, zoneId);
            DnsResourceRecordTypeEnum chainType = useNsec3
                ? DnsResourceRecordTypeEnum.Nsec3
                : DnsResourceRecordTypeEnum.Nsec;

            Assert.NotEmpty(signed.DenialOfExistenceRecords);
            Assert.All(
                signed.DenialOfExistenceRecords,
                record => Assert.Equal(300u, record.TimeToLive)
            );

            foreach (DnsResourceRecord chainRecord in signed.DenialOfExistenceRecords)
            {
                IReadOnlyList<DnsResourceRecord> signatures = signed.GetSignatures(
                    chainRecord.Name,
                    chainType
                );
                Assert.NotEmpty(signatures);
                Assert.All(
                    signatures,
                    signature =>
                        Assert.Equal(
                            300u,
                            (
                                (RrSigRecordData)
                                    DnsRecordDataCodecRegistry.Decode(
                                        signature.Type,
                                        signature.Data
                                    )
                            ).OriginalTimeToLiveSeconds
                        )
                );
            }

            // The DNSKEY RRset keeps the SOA's own TTL.
            Assert.All(signed.DnsKeyRecords, record => Assert.Equal(3600u, record.TimeToLive));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResignForScheduledExpiry_SignsEveryRrsetAgainWithFreshTimes(bool useNsec3)
    {
        (AuthoritativeTestZoneHost host, Guid zoneId) = await CreateHostAsync(useNsec3);
        await using (host)
        {
            SignedZoneCache cache = host.Services.GetRequiredService<SignedZoneCache>();
            SignedZoneData first = GetSigned(host, zoneId);

            // Signature times have a resolution of one second, so wait for the clock to move.
            await Task.Delay(TimeSpan.FromSeconds(1.2));
            await cache.ResignForScheduledExpiryAsync(zoneId);
            SignedZoneData second = GetSigned(host, zoneId);

            Assert.NotSame(first, second);
            Assert.NotEmpty(first.RrsigsByCoveredRrset);
            Assert.Equal(
                first.RrsigsByCoveredRrset.Keys.OrderBy(key => key.ToString()),
                second.RrsigsByCoveredRrset.Keys.OrderBy(key => key.ToString())
            );

            foreach (
                (
                    (DnsName Name, DnsResourceRecordTypeEnum Type) key,
                    _
                ) in first.RrsigsByCoveredRrset
            )
            {
                RrSigRecordData before = Decode(first.RrsigsByCoveredRrset[key][0]);
                RrSigRecordData after = Decode(second.RrsigsByCoveredRrset[key][0]);
                Assert.NotEqual(before.SignatureInception, after.SignatureInception);
            }
        }
    }

    /// <summary>
    ///     This method decodes the RDATA of an RRSIG record, so tests can compare signatures
    ///     before and after a re-sign.
    /// </summary>
    private static RrSigRecordData Decode(DnsResourceRecord record) =>
        (RrSigRecordData)DnsRecordDataCodecRegistry.Decode(record.Type, record.Data);

    /// <summary>
    ///     This method adds a TSIG key to the host and binds it to the zone, so an AXFR can
    ///     authenticate (a transfer needs a key bound in <see cref="ZoneEntity.TransferTsigKeyNames" />).
    /// </summary>
    private static async Task AllowTransferAsync(AuthoritativeTestZoneHost host, string keyName)
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        IZoneRepository zones = scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        ZoneEntity zone = await zones.FindByNameAsync(ZoneName);
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
    ///     This method performs a TSIG-signed AXFR against the host and returns every wire record
    ///     of the transfer.
    /// </summary>
    private static async Task<List<DnsResourceRecord>> PullAxfrAsync(
        AuthoritativeTestZoneHost host,
        string keyName
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
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse(ZoneName),
                    DnsResourceRecordTypeEnum.Axfr,
                    DnsClassEnum.Internet
                ),
            },
        };

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

        await foreach (DnsMessage message in transferService.CreateAxfrResponseAsync(context))
        {
            Assert.Equal(DnsResponseCodeEnum.NoError, message.ResponseCode);
            records.AddRange(message.Answers);
        }

        return records;
    }
}
