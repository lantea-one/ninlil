using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Authoritative.Tests.Resolution;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Dnssec.Algorithms;
using Ninlil.Dns.Server.Recursive.Validation;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Dnssec;

/// <summary>
///     This class checks what happens to a zone that is marked signed but has no key that can
///     sign because every key uses an algorithm prohibited for signing (RFC 8624 §3.1): the
///     signing service returns <see langword="null" />, <see cref="SignedZoneCache" /> keeps the
///     zone listed but serves it unsigned (no DNSKEY, RRSIG, NSEC or NSEC3) and logs a Warning,
///     a parent zone hosted here generates no DS for it and proves that with an NSEC or NSEC3
///     record (a DS query for the child), and adding a key with a permitted
///     algorithm makes the next reload serve it signed again. Each test builds a SQLite
///     <see cref="AuthoritativeTestZoneHost" /> and writes the RSASHA1 keys to storage directly,
///     because key generation refuses that algorithm.
/// </summary>
public sealed class ZoneSigningServiceNoSigningKeyTests
{
    /// <summary>
    ///     The name of the zone whose keys are prohibited.
    /// </summary>
    private const string DeadName = "nokey.test";

    /// <summary>
    ///     This class is a logger provider that keeps the Warning messages written by the
    ///     <see cref="SignedZoneCache" />.
    /// </summary>
    private sealed class CacheWarningCollector : ILoggerProvider, ILogger
    {
        /// <summary>The Warning messages, guarded by a lock on the list itself.</summary>
        public readonly List<string> Warnings = [];

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName) =>
            categoryName == typeof(SignedZoneCache).FullName
                ? this
                : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state) => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
                lock (Warnings)
                    Warnings.Add(formatter(state, exception));
        }

        /// <inheritdoc />
        public void Dispose() { }
    }

    /// <summary>
    ///     This method stores an Active RSASHA1 key of the given role straight in the key
    ///     repository, with a real key pair, as a key created before the prohibition would be.
    /// </summary>
    private static async Task AddRsaSha1KeyAsync(
        IServiceProvider services,
        Guid zoneId,
        DnssecKeyRoleEnum role
    )
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IDnssecKeyRepository keyRepository =
            scope.ServiceProvider.GetRequiredService<IDnssecKeyRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        DnssecAlgorithmRegistry.TryGetAlgorithm(
            DnssecAlgorithmEnum.RsaSha1,
            out IDnssecSigningAlgorithm rsaSha1
        );
        DnssecKeyPair pair = rsaSha1.GenerateKeyPair(2048);
        byte[] rdata = DnsRecordDataCodecRegistry.Encode(
            DnsResourceRecordTypeEnum.DnsKey,
            new DnsKeyRecordData(
                role == DnssecKeyRoleEnum.Ksk,
                false,
                DnssecAlgorithmEnum.RsaSha1,
                pair.PublicKeyWireFormat
            )
        );
        await keyRepository.AddAsync(
            new DnssecKeyEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = zoneId,
                Role = role,
                Algorithm = DnssecAlgorithmEnum.RsaSha1,
                PublicKeyWireFormat = pair.PublicKeyWireFormat,
                PrivateKeyPkcs8Encrypted = scope
                    .ServiceProvider.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("Ninlil.Dns.Server.Dnssec.PrivateKey")
                    .Protect(Convert.ToBase64String(pair.PrivateKeyPkcs8)),
                KeyTag = DnssecKeyTag.Compute(DnssecAlgorithmEnum.RsaSha1, rdata),
                LifecycleState = DnssecKeyLifecycleStateEnum.Active,
                PublishedAtUtc = DateTimeOffset.UtcNow,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    ///     This method adds a signed zone with only an RSASHA1 KSK and ZSK to the host.
    /// </summary>
    private static async Task<Guid> AddProhibitedOnlyZoneAsync(
        AuthoritativeTestZoneHost host,
        string name,
        IEnumerable<TestRecord> records
    )
    {
        Guid zoneId = await host.AddZoneAsync(
            name,
            records,
            TestZoneSigning.Nsec,
            generateKeys: false
        );
        await AddRsaSha1KeyAsync(host.Services, zoneId, DnssecKeyRoleEnum.Ksk);
        await AddRsaSha1KeyAsync(host.Services, zoneId, DnssecKeyRoleEnum.Zsk);

        return zoneId;
    }

    [Fact]
    public async Task SignZoneAsync_WithOnlyProhibitedKeys_ReturnsNull()
    {
        await using AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        Guid zoneId = await AddProhibitedOnlyZoneAsync(
            host,
            DeadName,
            [TestRecord.A($"www.{DeadName}", "192.0.2.1")]
        );

        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        IZoneSigningService signingService =
            scope.ServiceProvider.GetRequiredService<IZoneSigningService>();

        Assert.Null(await signingService.SignZoneAsync(zoneId));
    }

    [Fact]
    public async Task Queries_ForAZoneWithOnlyProhibitedKeys_AreServedUnsigned()
    {
        await using AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        Guid zoneId = await AddProhibitedOnlyZoneAsync(
            host,
            DeadName,
            [TestRecord.A($"www.{DeadName}", "192.0.2.1")]
        );
        await host.StartAsync();

        DnsMessage dnsKey = await host.QueryAsync(
            DeadName,
            DnsResourceRecordTypeEnum.DnsKey,
            dnssecOk: true
        );
        Assert.Equal(DnsResponseCodeEnum.NoError, dnsKey.ResponseCode);
        Assert.Empty(dnsKey.Answers);
        Assert.DoesNotContain(dnsKey.AuthorityRecords, IsDnssecRecord);

        DnsMessage address = await host.QueryAsync(
            $"www.{DeadName}",
            DnsResourceRecordTypeEnum.A,
            dnssecOk: true
        );
        Assert.Equal(DnsResponseCodeEnum.NoError, address.ResponseCode);
        Assert.Single(address.Answers, record => record.Type == DnsResourceRecordTypeEnum.A);
        Assert.DoesNotContain(address.Answers, IsDnssecRecord);
        Assert.DoesNotContain(address.AuthorityRecords, IsDnssecRecord);

        DnsMessage missing = await host.QueryAsync(
            $"absent.{DeadName}",
            DnsResourceRecordTypeEnum.A,
            dnssecOk: true
        );
        Assert.Equal(DnsResponseCodeEnum.NxDomain, missing.ResponseCode);
        Assert.DoesNotContain(missing.AuthorityRecords, IsDnssecRecord);

        SignedZoneCache cache = host.Services.GetRequiredService<SignedZoneCache>();
        Assert.Null(cache.GetSignedData(zoneId));
        Assert.Contains(zoneId, cache.CachedZoneIds);
    }

    [Fact]
    public async Task Start_ForAZoneWithOnlyProhibitedKeys_LogsOneWarningNamingTheZone()
    {
        CacheWarningCollector warnings = new();
        await using AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync(
            warnings
        );
        await AddProhibitedOnlyZoneAsync(host, DeadName, []);

        await host.StartAsync();

        string warning;
        lock (warnings.Warnings)
            warning = Assert.Single(warnings.Warnings);
        Assert.Contains(DeadName, warning, StringComparison.Ordinal);
        Assert.Contains("unsigned", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_ForAParentOfAChildWithOnlyProhibitedKeys_GeneratesNoDsForIt()
    {
        await using AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        Guid parentId = await host.AddZoneAsync(
            "par.test",
            [
                TestRecord.Ns("kid.par.test", "ns.provider.example."),
                TestRecord.A("www.par.test", "192.0.2.2"),
            ],
            TestZoneSigning.Nsec
        );
        await AddProhibitedOnlyZoneAsync(host, "kid.par.test", []);
        await host.StartAsync();

        SignedZoneData parent = host
            .Services.GetRequiredService<SignedZoneCache>()
            .GetSignedData(parentId);
        Assert.NotNull(parent);
        Assert.False(parent.DsRecordsByDelegationName.ContainsKey(DnsName.Parse("kid.par.test")));

        // The child zone is hosted here, so it answers names below it itself. The proof that the
        // parent has no DS is checked on the DS query below.
        DnsMessage referral = await host.QueryAsync(
            "www.kid.par.test",
            DnsResourceRecordTypeEnum.A,
            dnssecOk: true
        );
        Assert.DoesNotContain(
            referral.AuthorityRecords,
            record => record.Type == DnsResourceRecordTypeEnum.DS
        );
    }

    [Theory]
    [InlineData(TestZoneSigningKind.Nsec)]
    [InlineData(TestZoneSigningKind.Nsec3)]
    public async Task DsQuery_ForAChildWithOnlyProhibitedKeys_IsProvenInsecureByTheParent(
        TestZoneSigningKind kind
    )
    {
        await using AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        TestZoneSigning signing =
            kind == TestZoneSigningKind.Nsec3
                ? TestZoneSigning.Nsec3(1, "AB")
                : TestZoneSigning.Nsec;
        await host.AddZoneAsync(
            "par.test",
            [
                TestRecord.Ns("kid.par.test", "ns.provider.example."),
                TestRecord.A("www.par.test", "192.0.2.2"),
            ],
            signing
        );
        await AddProhibitedOnlyZoneAsync(host, "kid.par.test", []);
        await host.StartAsync();

        // The DS belongs to the parent (RFC 4035 §3.1.4.1), which has none for this child. The
        // child's own zone is served unsigned, so a validator must see an insecure delegation:
        // a NODATA answer with the parent's SOA, no DS and the NSEC (or the matching NSEC3)
        // record whose type bitmap lists NS but not DS (RFC 4035 §3.1.4, RFC 5155 §7.2.4).
        DnsMessage response = await host.QueryAsync(
            "kid.par.test",
            DnsResourceRecordTypeEnum.DS,
            dnssecOk: true
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.True(response.IsAuthoritativeAnswer);
        Assert.Empty(response.Answers);
        Assert.DoesNotContain(
            response.AuthorityRecords,
            record => record.Type == DnsResourceRecordTypeEnum.DS
        );
        Assert.Contains(
            response.AuthorityRecords,
            record => record.Type == DnsResourceRecordTypeEnum.Soa
        );

        DnsResourceRecordTypeEnum chainType =
            kind == TestZoneSigningKind.Nsec3
                ? DnsResourceRecordTypeEnum.Nsec3
                : DnsResourceRecordTypeEnum.Nsec;
        DnsResourceRecord proof = Assert.Single(
            response.AuthorityRecords,
            record => record.Type == chainType
        );
        Assert.Contains(
            response.AuthorityRecords,
            record => record.Type == DnsResourceRecordTypeEnum.RrSig && record.Name == proof.Name
        );

        IReadOnlyList<DnsResourceRecordTypeEnum> bitmap =
            kind == TestZoneSigningKind.Nsec3
                ? ((Nsec3RecordData)DnsRecordDataCodecRegistry.Decode(chainType, proof.Data)).Types
                : ((NsecRecordData)DnsRecordDataCodecRegistry.Decode(chainType, proof.Data)).Types;
        Assert.Contains(DnsResourceRecordTypeEnum.NS, bitmap);
        Assert.DoesNotContain(DnsResourceRecordTypeEnum.DS, bitmap);

        DenialOfExistenceOutcomeEnum outcome =
            kind == TestZoneSigningKind.Nsec3
                ? DenialOfExistenceValidator.VerifyNoDataNsec3(
                    DnsName.Parse("kid.par.test"),
                    DnsResourceRecordTypeEnum.DS,
                    DnsName.Parse("par.test"),
                    response.AuthorityRecords
                )
                : DenialOfExistenceValidator.VerifyNoData(
                    DnsName.Parse("kid.par.test"),
                    DnsResourceRecordTypeEnum.DS,
                    DnsName.Parse("par.test"),
                    response.AuthorityRecords
                );
        Assert.Equal(DenialOfExistenceOutcomeEnum.Proven, outcome);
    }

    [Fact]
    public async Task Reload_AfterAKeyWithAPermittedAlgorithmIsAdded_ServesTheZoneSignedAgain()
    {
        await using AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        Guid zoneId = await AddProhibitedOnlyZoneAsync(
            host,
            DeadName,
            [TestRecord.A($"www.{DeadName}", "192.0.2.1")]
        );
        await host.StartAsync();
        SignedZoneCache cache = host.Services.GetRequiredService<SignedZoneCache>();
        Assert.Null(cache.GetSignedData(zoneId));

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            IDnssecKeyManagementService keyManagement =
                scope.ServiceProvider.GetRequiredService<IDnssecKeyManagementService>();
            await keyManagement.GenerateKeyAsync(
                zoneId,
                DnssecKeyRoleEnum.Ksk,
                DnssecAlgorithmEnum.Ed25519
            );
            await keyManagement.GenerateKeyAsync(
                zoneId,
                DnssecKeyRoleEnum.Zsk,
                DnssecAlgorithmEnum.Ed25519
            );
        }

        await cache.ReloadZoneAsync(zoneId);

        SignedZoneData signed = cache.GetSignedData(zoneId);
        Assert.NotNull(signed);
        // Only the two Ed25519 keys are published; the RSASHA1 keys never are.
        Assert.Equal(2, signed.DnsKeyRecords.Count);
        DnsMessage dnsKey = await host.QueryAsync(
            DeadName,
            DnsResourceRecordTypeEnum.DnsKey,
            dnssecOk: true
        );
        Assert.Contains(dnsKey.Answers, record => record.Type == DnsResourceRecordTypeEnum.DnsKey);
        Assert.Contains(dnsKey.Answers, record => record.Type == DnsResourceRecordTypeEnum.RrSig);
    }

    /// <summary>
    ///     This method reports whether a record is a DNSSEC record that an unsigned zone must
    ///     not serve.
    /// </summary>
    private static bool IsDnssecRecord(DnsResourceRecord record) =>
        record.Type
            is DnsResourceRecordTypeEnum.DnsKey
                or DnsResourceRecordTypeEnum.RrSig
                or DnsResourceRecordTypeEnum.Nsec
                or DnsResourceRecordTypeEnum.Nsec3
                or DnsResourceRecordTypeEnum.Nsec3Param;
}
