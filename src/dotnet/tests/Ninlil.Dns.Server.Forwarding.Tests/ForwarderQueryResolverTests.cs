using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Dnssec.Algorithms;
using Ninlil.Dns.Server.Forwarding.Clients;
using Ninlil.Dns.Server.Forwarding.Definition;
using Ninlil.Dns.Server.Forwarding.Tests.Fakes;
using Ninlil.Dns.Server.Recursive;
using Ninlil.Dns.Server.Recursive.Caching;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Validation;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This class proves <see cref="ForwarderQueryResolver" /> end-to-end: forwarder-set
///     resolution (global vs. per-zone override), real network round trips against a minimal
///     fake UDP responder standing in for an upstream, and DNSSEC re-validation of a forwarded
///     answer via the real <see cref="DnssecValidator" />.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
public sealed partial class ForwarderQueryResolverTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public ForwarderQueryResolverTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        ServiceCollection services = new();
        services.AddDbContext<NinlilDnsContext>(builder => builder.UseSqlite(_connection));
        services.AddScoped<IZoneRepository, ZoneRepository>();
        services.AddScoped<IDnsForwarderRepository, DnsForwarderRepository>();
        services.AddScoped<IUnitOfWork, EntityFrameworkUnitOfWork>();

        // The filtering repositories are only used by the CNAME-cloaking tests, which run a
        // forwarded answer through the real filter engine.
        services.AddScoped<IDnsFilterListRepository, DnsFilterListRepository>();
        services.AddScoped<IDnsFilterListEntryRepository, DnsFilterListEntryRepository>();
        services.AddScoped<IDnsClientGroupRepository, DnsClientGroupRepository>();
        services.AddScoped<IDnsClientGroupNetworkRepository, DnsClientGroupNetworkRepository>();
        services.AddScoped<
            IDnsClientGroupListAssignmentRepository,
            DnsClientGroupListAssignmentRepository
        >();
        _provider = services.BuildServiceProvider();

        using IServiceScope scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<NinlilDnsContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task TryResolveAsync_WithNoForwardersConfigured_ReturnsNotHandled()
    {
        ForwarderQueryResolver resolver = CreateResolver();

        DnsQueryResolution resolution = await resolver.TryResolveAsync(
            BuildContext(BuildQuery(1, "example.com."))
        );

        Assert.False(resolution.IsHandled);
    }

    [Fact]
    public async Task TryResolveAsync_WithEveryForwarderUnreachable_ReturnsNotHandled()
    {
        await SeedForwarderAsync("127.0.0.1:1");

        ForwarderQueryResolver resolver = CreateResolver(
            forwardingOptions: new ForwardingOptions
            {
                Enable = true,
                FailoverStrategy = ForwarderFailoverStrategyEnum.TryInOrder,
                PerForwarderTimeout = TimeSpan.FromSeconds(1),
            }
        );

        DnsQueryResolution resolution = await resolver.TryResolveAsync(
            BuildContext(BuildQuery(1, "example.com."))
        );

        Assert.False(resolution.IsHandled);
    }

    [Fact]
    public async Task TryResolveAsync_WithAGlobalForwarderAnsweringForReal_ReturnsHandledWithTheRealAnswer()
    {
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.10"))
        );

        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");

        ForwarderQueryResolver resolver = CreateResolver();

        DnsQueryResolution resolution = await resolver.TryResolveAsync(
            BuildContext(BuildQuery(42, "example.com."))
        );

        Assert.True(resolution.IsHandled);
        Assert.Equal(42, resolution.Response.Id);
        Assert.Equal(DnsResponseCodeEnum.NoError, resolution.Response.ResponseCode);
        ARecordData answer = (ARecordData)
            DnsRecordDataCodecRegistry.Decode(
                DnsResourceRecordTypeEnum.A,
                resolution.Response.Answers[0].Data
            );
        Assert.Equal(IPAddress.Parse("192.0.2.10"), answer.Address);
    }

    /// <summary>
    ///     This test proves a structural invariant: <see cref="ForwarderQueryResolver" />
    ///     is registered (and therefore constructed) regardless of
    ///     <see cref="ForwardingOptions.Enable" />'s own value at process startup, with its own
    ///     already-existing live check (<c>TryResolveAsync</c>'s very first line) reading
    ///     <c>IOptionsMonitor</c>'s <c>CurrentValue</c> fresh on every query -- so the *same,
    ///     already-constructed* resolver instance genuinely starts forwarding once
    ///     <see cref="ForwardingOptions.Enable" /> flips to <see langword="true" /> on the shared
    ///     options object it was built against, with no restart and no new instance, mirroring
    ///     <c>RecursiveOptionsHotReloadTests</c>'s own "same instance, before/after" proof shape
    ///     for <c>RecursiveServerOptions</c>.
    /// </summary>
    [Fact]
    public async Task TryResolveAsync_WhenEnableFlipsLiveOnTheSharedOptionsObject_TheSameResolverInstanceStartsForwarding()
    {
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.10"))
        );
        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");

        ForwardingOptions sharedOptions = new()
        {
            Enable = false,
            FailoverStrategy = ForwarderFailoverStrategyEnum.TryInOrder,
            PerForwarderTimeout = TimeSpan.FromSeconds(2),
        };
        ForwarderQueryResolver resolver = CreateResolver(forwardingOptions: sharedOptions);

        // Before: a real, reachable forwarder is configured, but the resolver is disabled, so its
        // own live Enable check declines outright -- this is the "structural" half of the proof,
        // distinguishing "no forwarder answered" from "declined because disabled."
        DnsQueryResolution before = await resolver.TryResolveAsync(
            BuildContext(BuildQuery(7, "example.com."))
        );
        Assert.False(before.IsHandled);

        sharedOptions.Enable = true;

        // After: the identical resolver instance, with no reconstruction, now genuinely reaches
        // the same upstream and returns its real answer.
        DnsQueryResolution after = await resolver.TryResolveAsync(
            BuildContext(BuildQuery(7, "example.com."))
        );
        Assert.True(after.IsHandled);
        Assert.Equal(DnsResponseCodeEnum.NoError, after.Response.ResponseCode);
    }

    [Fact]
    public async Task TryResolveAsync_WithAForwarderTypeZone_PrefersItOverTheGlobalSet()
    {
        using FakeUdpUpstream globalUpstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.1"))
        );
        using FakeUdpUpstream zoneUpstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("203.0.113.99"))
        );

        await SeedForwarderAsync($"127.0.0.1:{globalUpstream.Port}");
        await SeedForwarderZoneAsync("example.com", $"127.0.0.1:{zoneUpstream.Port}");

        ForwarderQueryResolver resolver = CreateResolver();

        DnsQueryResolution resolution = await resolver.TryResolveAsync(
            BuildContext(BuildQuery(7, "www.example.com."))
        );

        Assert.True(resolution.IsHandled);
        ARecordData answer = (ARecordData)
            DnsRecordDataCodecRegistry.Decode(
                DnsResourceRecordTypeEnum.A,
                resolution.Response.Answers[0].Data
            );
        Assert.Equal(IPAddress.Parse("203.0.113.99"), answer.Address);
    }

    /// <summary>
    ///     This test proves a <see cref="ZoneTypeEnum.SecondaryForwarder" /> zone's own
    ///     <see cref="ZoneEntity.ForwarderAddresses" /> -- populated, in real deployment, by a real
    ///     AXFR/IXFR pull from its configured <see cref="ZoneTypeEnum.Forwarder" /> primary and
    ///     decoded back from that primary's own apex TXT records (see
    ///     <c>Ninlil.Dns.Server.Authoritative.Tests.Forwarding.SecondaryForwarderZoneTransferTests</c>
    ///     for that real, two-host transfer end to end) -- is honored by
    ///     <see cref="ForwarderQueryResolver" /> exactly like an ordinary
    ///     <see cref="ZoneTypeEnum.Forwarder" /> zone's own list: a real query genuinely reaches
    ///     the configured upstream and returns its real answer.
    /// </summary>
    [Fact]
    public async Task TryResolveAsync_WithASecondaryForwarderTypeZone_ForwardsUsingItsOwnAddresses()
    {
        using FakeUdpUpstream zoneUpstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("198.51.100.42"))
        );

        await SeedForwarderZoneAsync(
            "secondary-forward.example.com",
            ZoneTypeEnum.SecondaryForwarder,
            $"127.0.0.1:{zoneUpstream.Port}"
        );

        ForwarderQueryResolver resolver = CreateResolver();

        DnsQueryResolution resolution = await resolver.TryResolveAsync(
            BuildContext(BuildQuery(9, "www.secondary-forward.example.com."))
        );

        Assert.True(resolution.IsHandled);
        ARecordData answer = (ARecordData)
            DnsRecordDataCodecRegistry.Decode(
                DnsResourceRecordTypeEnum.A,
                resolution.Response.Answers[0].Data
            );
        Assert.Equal(IPAddress.Parse("198.51.100.42"), answer.Address);
    }

    [Fact]
    public async Task TryResolveAsync_ReValidatesDnssec_AcceptingSecureAndRejectingBogus()
    {
        DnssecAlgorithmRegistry.TryGetAlgorithm(
            DnssecAlgorithmEnum.Ed25519,
            out IDnssecSigningAlgorithm algorithm
        );

        DnssecKeyPair rootKey = algorithm.GenerateKeyPair();
        DnsKeyRecordData rootDnsKeyData = new(
            true,
            false,
            DnssecAlgorithmEnum.Ed25519,
            rootKey.PublicKeyWireFormat
        );
        byte[] rootDnsKeyRdata = DnsRecordDataCodecRegistry.Encode(
            DnsResourceRecordTypeEnum.DnsKey,
            rootDnsKeyData
        );
        DnsResourceRecord rootDnsKeyRecord = new(
            DnsName.Root,
            DnsResourceRecordTypeEnum.DnsKey,
            DnsClassEnum.Internet,
            3600,
            rootDnsKeyRdata
        );
        ushort rootKeyTag = DnssecKeyTag.Compute(DnssecAlgorithmEnum.Ed25519, rootDnsKeyRdata);
        DnsResourceRecord rootDnsKeyRrsig = Sign(
            algorithm,
            rootKey,
            rootKeyTag,
            DnsName.Root,
            DnsName.Root,
            [rootDnsKeyRecord]
        );

        DnsName queriedName = DnsName.Parse("example.com");
        DnsResourceRecord answer = new(
            queriedName,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.A,
                new ARecordData(IPAddress.Parse("192.0.2.55"))
            )
        );
        DnsResourceRecord genuineRrsig = Sign(
            algorithm,
            rootKey,
            rootKeyTag,
            DnsName.Root,
            queriedName,
            [answer]
        );

        FakeIterativeResolutionEngine engine = new();
        engine.AddResult(
            DnsName.Root,
            DnsResourceRecordTypeEnum.DnsKey,
            new IterativeResolutionResult(
                RecursiveResolutionStatusEnum.Success,
                [rootDnsKeyRecord],
                [rootDnsKeyRrsig],
                null,
                [DnsName.Root]
            )
        );

        TrustAnchor trustAnchor = new(
            rootKeyTag,
            DnssecAlgorithmEnum.Ed25519,
            2,
            DsDigestCodec.Compute(2, DnsName.Root, rootDnsKeyRdata)
        );
        DnssecValidator realValidator = new(
            engine,
            new EmptyRecursiveCacheStub(),
            new FixedTrustAnchorStoreStub([trustAnchor])
        );

        // Secure case: the upstream returns the genuinely signed answer.
        using (
            FakeUdpUpstream secureUpstream = FakeUdpUpstream.Start(
                BuildSignedPositiveResponse(answer, genuineRrsig)
            )
        )
        {
            await SeedForwarderAsync($"127.0.0.1:{secureUpstream.Port}");

            ForwarderQueryResolver secureResolver = CreateResolver(
                validator: realValidator,
                recursiveOptions: new RecursiveServerOptions { EnableDnssecValidation = true }
            );

            DnsQueryResolution secureResolution = await secureResolver.TryResolveAsync(
                BuildContext(BuildQuery(1, "example.com."))
            );

            Assert.True(secureResolution.IsHandled);
        }

        // Bogus case: a second global forwarder is the ONLY configured one, returning an
        // answer whose RRSIG signature has been tampered with -- ForwarderQueryResolver must
        // reject it (treat it as a failed attempt), so nothing is left to try and the overall
        // result is NotHandled.
        await using NinlilDnsContext resetContext = _provider
            .GetRequiredService<IServiceScopeFactory>()
            .CreateScope()
            .ServiceProvider.GetRequiredService<NinlilDnsContext>();
        resetContext.RemoveRange(resetContext.Set<DnsForwarderEntity>());
        await resetContext.SaveChangesAsync();

        RrSigRecordData genuineRrsigData = (RrSigRecordData)
            DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.RrSig, genuineRrsig.Data);
        byte[] tamperedSignature = [.. genuineRrsigData.Signature];
        tamperedSignature[0] ^= 0xFF;
        DnsResourceRecord tamperedRrsig = new(
            queriedName,
            DnsResourceRecordTypeEnum.RrSig,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.RrSig,
                new RrSigRecordData(
                    genuineRrsigData.TypeCovered,
                    genuineRrsigData.Algorithm,
                    genuineRrsigData.Labels,
                    genuineRrsigData.OriginalTimeToLiveSeconds,
                    genuineRrsigData.SignatureExpiration,
                    genuineRrsigData.SignatureInception,
                    genuineRrsigData.KeyTag,
                    genuineRrsigData.SignerName,
                    tamperedSignature
                )
            )
        );

        using FakeUdpUpstream bogusUpstream = FakeUdpUpstream.Start(
            BuildSignedPositiveResponse(answer, tamperedRrsig)
        );
        await SeedForwarderAsync($"127.0.0.1:{bogusUpstream.Port}");

        ForwarderQueryResolver bogusResolver = CreateResolver(
            validator: realValidator,
            recursiveOptions: new RecursiveServerOptions { EnableDnssecValidation = true }
        );

        DnsQueryResolution bogusResolution = await bogusResolver.TryResolveAsync(
            BuildContext(BuildQuery(2, "example.com."))
        );

        Assert.False(bogusResolution.IsHandled);
    }

    private ForwarderQueryResolver CreateResolver(
        ForwardingOptions forwardingOptions = null,
        RecursiveServerOptions recursiveOptions = null,
        IDnssecValidator validator = null
    )
    {
        ForwardingOptions effectiveForwardingOptions =
            forwardingOptions
            ?? new ForwardingOptions
            {
                Enable = true,
                FailoverStrategy = ForwarderFailoverStrategyEnum.TryInOrder,
                PerForwarderTimeout = TimeSpan.FromSeconds(2),
            };

        IReadOnlyList<IUpstreamForwarderClient> clientList =
        [
            new PlainForwarderClient(),
            new TlsForwarderClient(),
            new HttpsForwarderClient(),
            new QuicForwarderClient(),
            new DnsCryptForwarderClient(),
        ];

        ForwarderHealthTracker tracker = new(
            clientList,
            new StaticOptionsMonitor<ForwardingOptions>(effectiveForwardingOptions),
            NullLogger<ForwarderHealthTracker>.Instance
        );

        return new ForwarderQueryResolver(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            clientList,
            new UpstreamForwarderDefinitionParser(),
            tracker,
            validator ?? new AlwaysInsecureDnssecValidatorStub(),
            new StaticOptionsMonitor<ForwardingOptions>(effectiveForwardingOptions),
            new StaticOptionsMonitor<RecursiveServerOptions>(
                recursiveOptions ?? new RecursiveServerOptions { EnableDnssecValidation = false }
            ),
            NullLogger<ForwarderQueryResolver>.Instance
        );
    }

    private async Task SeedForwarderAsync(string definitionString, int sortOrder = 0)
    {
        using IServiceScope scope = _provider.CreateScope();
        IDnsForwarderRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsForwarderRepository>();

        await repository.AddAsync(
            new DnsForwarderEntity
            {
                Id = Guid.NewGuid(),
                Name = $"forwarder-{Guid.NewGuid():N}",
                DefinitionString = definitionString,
                SortOrder = sortOrder,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private async Task SeedForwarderZoneAsync(
        string zoneName,
        params string[] forwarderAddresses
    ) => await SeedForwarderZoneAsync(zoneName, ZoneTypeEnum.Forwarder, forwarderAddresses);

    /// <summary>
    ///     This method seeds a <see cref="ZoneTypeEnum.Forwarder" /> or
    ///     <see cref="ZoneTypeEnum.SecondaryForwarder" /> zone directly with its own
    ///     <see cref="ZoneEntity.ForwarderAddresses" /> already populated -- standing in for a
    ///     <see cref="ZoneTypeEnum.SecondaryForwarder" /> zone's own real AXFR/IXFR pull (covered
    ///     end-to-end, over a real two-host TCP transfer, by
    ///     <c>Ninlil.Dns.Server.Authoritative.Tests.Forwarding.SecondaryForwarderZoneTransferTests</c>)
    ///     so this class's own tests can prove <see cref="ForwarderQueryResolver" /> itself treats
    ///     the two zone types identically once that list is populated, with no real transfer
    ///     needed here at all.
    /// </summary>
    private async Task SeedForwarderZoneAsync(
        string zoneName,
        ZoneTypeEnum type,
        params string[] forwarderAddresses
    )
    {
        using IServiceScope scope = _provider.CreateScope();
        IZoneRepository repository = scope.ServiceProvider.GetRequiredService<IZoneRepository>();

        await repository.AddAsync(
            new ZoneEntity
            {
                Id = Guid.NewGuid(),
                Name = zoneName,
                Type = type,
                ForwarderAddresses = [.. forwarderAddresses],
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private static DnsMessage BuildQuery(ushort id, string name) =>
        new()
        {
            Id = id,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse(name),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };

    private static DnsQueryContext BuildContext(DnsMessage request) =>
        new(request, DnsTransportProtocolEnum.Udp, IPAddress.Loopback, DateTimeOffset.UtcNow);

    private static byte[] BuildPositiveResponse(IPAddress address)
    {
        DnsMessage response = new()
        {
            IsResponse = true,
            ResponseCode = DnsResponseCodeEnum.NoError,
        };
        response.Answers.Add(
            new DnsResourceRecord(
                DnsName.Parse("example.com"),
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet,
                300,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.A,
                    new ARecordData(address)
                )
            )
        );

        return DnsMessageWriter.Write(response);
    }

    private static byte[] BuildSignedPositiveResponse(
        DnsResourceRecord answer,
        DnsResourceRecord rrsig
    )
    {
        DnsMessage response = new()
        {
            IsResponse = true,
            ResponseCode = DnsResponseCodeEnum.NoError,
        };
        response.Answers.Add(answer);
        response.Answers.Add(rrsig);

        return DnsMessageWriter.Write(response);
    }

    private static DnsResourceRecord Sign(
        IDnssecSigningAlgorithm algorithm,
        DnssecKeyPair key,
        ushort keyTag,
        DnsName signerName,
        DnsName ownerName,
        IReadOnlyList<DnsResourceRecord> rrset
    )
    {
        DnsResourceRecord first = rrset[0];
        DateTimeOffset inception = DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset expiration = DateTimeOffset.UtcNow.AddDays(30);

        RrSigRecordData unsigned = new(
            first.Type,
            DnssecAlgorithmEnum.Ed25519,
            (byte)ownerName.Labels.Count,
            first.TimeToLive,
            (uint)expiration.ToUnixTimeSeconds(),
            (uint)inception.ToUnixTimeSeconds(),
            keyTag,
            signerName,
            []
        );

        byte[] signingInput = DnssecCanonicalization.BuildSigningInput(
            unsigned,
            first.Name,
            first.Class,
            rrset
        );
        byte[] signature = algorithm.Sign(signingInput, key.PrivateKeyPkcs8);

        RrSigRecordData signed = new(
            unsigned.TypeCovered,
            unsigned.Algorithm,
            unsigned.Labels,
            unsigned.OriginalTimeToLiveSeconds,
            unsigned.SignatureExpiration,
            unsigned.SignatureInception,
            unsigned.KeyTag,
            unsigned.SignerName,
            signature
        );

        return new DnsResourceRecord(
            first.Name,
            DnsResourceRecordTypeEnum.RrSig,
            first.Class,
            first.TimeToLive,
            DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.RrSig, signed)
        );
    }

    /// <summary>
    ///     This class always reports Insecure -- the default validator stand-in for every test
    ///     that does not itself exercise DNSSEC re-validation, since <see cref="RecursiveServerOptions.EnableDnssecValidation" />
    ///     is left off for those.
    /// </summary>
    private sealed class AlwaysInsecureDnssecValidatorStub : IDnssecValidator
    {
        public Task<DnssecValidationResult> ValidateAsync(
            DnsName queriedName,
            DnsResourceRecordTypeEnum queriedType,
            DnsClassEnum recordClass,
            IterativeResolutionResult result,
            QueryBudget budget,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(DnssecValidationResult.Insecure());
    }

    /// <summary>This class always reports a cache miss, forcing <c>DnssecValidator</c>'s own <c>FindOwningZone</c> fallback to resolve to the root.</summary>
    private sealed class EmptyRecursiveCacheStub : IRecursiveCache
    {
        public RecursiveCacheEntry TryGet(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass
        ) => null;

        public RecursiveCacheEntry TryGetStale(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass,
            TimeSpan maxStaleAge
        ) => null;

        public void StorePositive(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass,
            IReadOnlyList<DnsResourceRecord> records,
            IReadOnlyList<DnsResourceRecord> dnssecRecords
        ) { }

        public void StoreNegative(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass,
            DnsResponseCodeEnum responseCode,
            DnsResourceRecord soa,
            IReadOnlyList<DnsResourceRecord> dnssecRecords
        ) { }

        public int EvictExpiredAndOverCapacity() => 0;

        public IReadOnlyList<(
            DnsName Name,
            DnsResourceRecordTypeEnum Type,
            DnsClassEnum Class
        )> GetPrefetchCandidates(TimeSpan prefetchWindow, TimeSpan recentAccessWindow) => [];
    }

    private sealed class FixedTrustAnchorStoreStub(IReadOnlyList<TrustAnchor> anchors)
        : ITrustAnchorStore
    {
        public IReadOnlyList<TrustAnchor> GetValidAnchors() => anchors;

        public IReadOnlyList<PersistedTrustAnchor> GetAll() => [];

        public void Save(IReadOnlyList<PersistedTrustAnchor> anchors) { }
    }

    private sealed class FakeIterativeResolutionEngine : IIterativeResolutionEngine
    {
        private readonly Dictionary<
            (DnsName Name, DnsResourceRecordTypeEnum Type),
            IterativeResolutionResult
        > _results = [];

        public void AddResult(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            IterativeResolutionResult result
        ) => _results[(name, type)] = result;

        public Task<IterativeResolutionResult> ResolveAsync(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass,
            bool requestDnssec,
            DnsEdnsOption clientSubnetOption = null,
            CancellationToken stoppingToken = default
        ) => Resolve(name, type);

        public Task<IterativeResolutionResult> ResolveAsync(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass,
            bool requestDnssec,
            QueryBudget budget,
            bool forceRootStart,
            DnsEdnsOption clientSubnetOption = null,
            CancellationToken stoppingToken = default
        ) => Resolve(name, type);

        public Task<IterativeResolutionResult> PrefetchAsync(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass,
            bool requestDnssec,
            CancellationToken stoppingToken = default
        ) => Resolve(name, type);

        private Task<IterativeResolutionResult> Resolve(
            DnsName name,
            DnsResourceRecordTypeEnum type
        ) =>
            Task.FromResult(
                _results.TryGetValue((name, type), out IterativeResolutionResult result)
                    ? result
                    : IterativeResolutionResult.ServFail()
            );
    }

    /// <summary>
    ///     This class is a minimal fake upstream DNS server: it binds a real UDP socket on an
    ///     ephemeral loopback port, and answers every received datagram with the exact same
    ///     canned response bytes, regardless of what was asked -- enough to exercise
    ///     <see cref="PlainForwarderClient" />/<see cref="ForwarderQueryResolver" /> for real over
    ///     a real socket, without needing this project's own full listener/pipeline stack.
    /// </summary>
    private sealed class FakeUdpUpstream : IDisposable
    {
        private readonly Socket _socket;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        private FakeUdpUpstream(Socket socket, byte[] responseBytes)
        {
            _socket = socket;
            _loop = RunAsync(responseBytes, _cts.Token);
        }

        public int Port => ((IPEndPoint)_socket.LocalEndPoint).Port;

        public static FakeUdpUpstream Start(byte[] responseBytes)
        {
            Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

            return new FakeUdpUpstream(socket, responseBytes);
        }

        private async Task RunAsync(byte[] responseBytes, CancellationToken stoppingToken)
        {
            byte[] buffer = new byte[4096];

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    SocketReceiveFromResult received = await _socket.ReceiveFromAsync(
                        buffer,
                        new IPEndPoint(IPAddress.Any, 0),
                        stoppingToken
                    );

                    // Echo the real query's own transaction ID (the DNS header's first two
                    // octets, RFC 1035 §4.1.1) into the canned response before sending it back --
                    // exactly what a real forwarder does, and what PlainForwarderClient's own
                    // anti-spoofing hardening now requires a genuine answer to match.
                    byte[] response = (byte[])responseBytes.Clone();
                    response[0] = buffer[0];
                    response[1] = buffer[1];

                    await _socket.SendToAsync(response, received.RemoteEndPoint, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when Dispose() cancels the loop.
            }
            catch (SocketException)
            {
                // Expected when Dispose() closes the socket while a receive is pending.
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _socket.Close();
            _cts.Dispose();
        }
    }
}
