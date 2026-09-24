using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.QueryLog.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog.Tests;

/// <summary>
///     This class covers <see cref="DnsQueryLogSubscriber" /> against a real, SQLite-backed
///     repository stack -- confirming a real event is actually turned into a real, correctly
///     mapped stored row, and, just as importantly, that disabling query logging genuinely
///     results in zero new rows rather than merely fewer.
/// </summary>
public sealed class DnsQueryLogSubscriberTests : IAsyncLifetime
{
    private readonly QueryLogTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    private static DnsQueryAnsweredEvent NewAnsweredEvent() =>
        new(
            OccurredAtUtc: new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
            Protocol: DnsTransportProtocolEnum.Udp,
            RemoteAddress: IPAddress.Parse("198.51.100.7"),
            QueriedName: "host.example.test",
            QueriedType: DnsResourceRecordTypeEnum.A,
            ResponseCode: DnsResponseCodeEnum.NoError,
            AnswerCount: 1
        );

    /// <summary>
    ///     This method waits, briefly, for the subscriber's own detached background persistence
    ///     task to finish -- <see cref="DnsQueryLogSubscriber.HandleAsync" /> itself only ever
    ///     starts that task and returns immediately, per architectural invariant #3, so a test
    ///     observing the write's own effect must give it a moment to actually land.
    /// </summary>
    private static async Task<IReadOnlyList<DnsQueryLogEntryEntity>> WaitForEntriesAsync(
        IServiceProvider services
    )
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            IDnsQueryLogRepository repository =
                scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();
            IReadOnlyList<DnsQueryLogEntryEntity> entries = await repository.ListAsync();

            if (entries.Count > 0)
                return entries;

            await Task.Delay(20);
        }

        await using AsyncServiceScope finalScope = services.CreateAsyncScope();
        return await finalScope
            .ServiceProvider.GetRequiredService<IDnsQueryLogRepository>()
            .ListAsync();
    }

    [Fact]
    public async Task HandleAsync_WhenEnabled_PersistsAMatchingEntry()
    {
        DnsQueryLogSubscriber subscriber = new(
            _testHost.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<QueryLoggingOptions>(
                new QueryLoggingOptions { Enable = true }
            ),
            NullLogger<DnsQueryLogSubscriber>.Instance
        );

        DnsQueryAnsweredEvent domainEvent = NewAnsweredEvent();
        await subscriber.HandleAsync(domainEvent, CancellationToken.None);

        IReadOnlyList<DnsQueryLogEntryEntity> entries = await WaitForEntriesAsync(
            _testHost.Host.Services
        );

        DnsQueryLogEntryEntity entry = Assert.Single(entries);
        Assert.Equal(domainEvent.OccurredAtUtc, entry.OccurredAtUtc);
        Assert.Equal("198.51.100.7", entry.ClientAddress);
        Assert.Equal(DnsTransportProtocolEnum.Udp, entry.Protocol);
        Assert.Equal("host.example.test", entry.QueriedName);
        Assert.Equal(DnsResourceRecordTypeEnum.A, entry.QueriedType);
        Assert.Equal(DnsResponseCodeEnum.NoError, entry.ResponseCode);
        Assert.Equal(1, entry.AnswerCount);
    }

    [Fact]
    public async Task HandleAsync_WhenDisabled_PersistsNothingAtAll()
    {
        DnsQueryLogSubscriber subscriber = new(
            _testHost.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<QueryLoggingOptions>(
                new QueryLoggingOptions { Enable = false }
            ),
            NullLogger<DnsQueryLogSubscriber>.Instance
        );

        await subscriber.HandleAsync(NewAnsweredEvent(), CancellationToken.None);

        // A disabled subscriber returns without ever opening a scope at all, so there is no
        // asynchronous write to wait out here -- a short, fixed delay is enough to prove nothing
        // eventually shows up, which a poll-until-non-empty loop (as WaitForEntriesAsync uses for
        // the enabled case) cannot itself express for a "nothing happens" assertion.
        await Task.Delay(200);

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();
        IReadOnlyList<DnsQueryLogEntryEntity> entries = await repository.ListAsync();

        Assert.Empty(entries);
    }

    [Fact]
    public async Task HandleAsync_WithNoRemoteAddress_PersistsANullClientAddress()
    {
        DnsQueryLogSubscriber subscriber = new(
            _testHost.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<QueryLoggingOptions>(
                new QueryLoggingOptions { Enable = true }
            ),
            NullLogger<DnsQueryLogSubscriber>.Instance
        );

        DnsQueryAnsweredEvent domainEvent = new(
            OccurredAtUtc: DateTimeOffset.UtcNow,
            Protocol: DnsTransportProtocolEnum.Tcp,
            RemoteAddress: null,
            QueriedName: null,
            QueriedType: null,
            ResponseCode: DnsResponseCodeEnum.FormatError,
            AnswerCount: 0
        );

        await subscriber.HandleAsync(domainEvent, CancellationToken.None);

        IReadOnlyList<DnsQueryLogEntryEntity> entries = await WaitForEntriesAsync(
            _testHost.Host.Services
        );

        DnsQueryLogEntryEntity entry = Assert.Single(entries);
        Assert.Null(entry.ClientAddress);
        Assert.Null(entry.QueriedName);
        Assert.Null(entry.QueriedType);
        Assert.Equal(DnsResponseCodeEnum.FormatError, entry.ResponseCode);
    }

    /// <summary>
    ///     This test proves <see cref="QueryLogPrivacyLevelEnum.DomainRedacted" /> genuinely never
    ///     stores the queried domain name at all -- not merely omits it from a later read -- while
    ///     every other field, including the client's own address and the queried type, is stored
    ///     exactly as observed, so aggregate stats by type/response code remain meaningful.
    /// </summary>
    [Fact]
    public async Task HandleAsync_AtDomainRedactedLevel_OmitsOnlyTheDomainName()
    {
        DnsQueryLogSubscriber subscriber = new(
            _testHost.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<QueryLoggingOptions>(
                new QueryLoggingOptions
                {
                    Enable = true,
                    PrivacyLevel = QueryLogPrivacyLevelEnum.DomainRedacted,
                }
            ),
            NullLogger<DnsQueryLogSubscriber>.Instance
        );

        await subscriber.HandleAsync(NewAnsweredEvent(), CancellationToken.None);

        IReadOnlyList<DnsQueryLogEntryEntity> entries = await WaitForEntriesAsync(
            _testHost.Host.Services
        );

        DnsQueryLogEntryEntity entry = Assert.Single(entries);
        Assert.Null(entry.QueriedName);
        Assert.Equal("198.51.100.7", entry.ClientAddress);
        Assert.Equal(DnsResourceRecordTypeEnum.A, entry.QueriedType);
        Assert.Equal(DnsResponseCodeEnum.NoError, entry.ResponseCode);
        Assert.Equal(1, entry.AnswerCount);
    }

    /// <summary>
    ///     This test proves <see cref="QueryLogPrivacyLevelEnum.ClientAnonymized" /> stores an
    ///     IPv4 client address truncated to its own containing /24 subnet (the last octet
    ///     zeroed), never the real address, on top of redacting the domain name exactly like
    ///     <see cref="QueryLogPrivacyLevelEnum.DomainRedacted" /> already does -- each level is
    ///     strictly more private than the one below it.
    /// </summary>
    [Fact]
    public async Task HandleAsync_AtClientAnonymizedLevel_TruncatesAnIPv4AddressToItsSubnet()
    {
        DnsQueryLogSubscriber subscriber = new(
            _testHost.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<QueryLoggingOptions>(
                new QueryLoggingOptions
                {
                    Enable = true,
                    PrivacyLevel = QueryLogPrivacyLevelEnum.ClientAnonymized,
                }
            ),
            NullLogger<DnsQueryLogSubscriber>.Instance
        );

        await subscriber.HandleAsync(NewAnsweredEvent(), CancellationToken.None);

        IReadOnlyList<DnsQueryLogEntryEntity> entries = await WaitForEntriesAsync(
            _testHost.Host.Services
        );

        DnsQueryLogEntryEntity entry = Assert.Single(entries);
        Assert.Equal("198.51.100.0", entry.ClientAddress);
        Assert.Null(entry.QueriedName);
    }

    /// <summary>
    ///     This test proves the same truncation for an IPv6 client address: the trailing 64 bits
    ///     (the last four groups) are zeroed, leaving only the /64 network prefix.
    /// </summary>
    [Fact]
    public async Task HandleAsync_AtClientAnonymizedLevel_TruncatesAnIPv6AddressToItsSubnet()
    {
        DnsQueryLogSubscriber subscriber = new(
            _testHost.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<QueryLoggingOptions>(
                new QueryLoggingOptions
                {
                    Enable = true,
                    PrivacyLevel = QueryLogPrivacyLevelEnum.ClientAnonymized,
                }
            ),
            NullLogger<DnsQueryLogSubscriber>.Instance
        );

        DnsQueryAnsweredEvent domainEvent = NewAnsweredEvent() with
        {
            RemoteAddress = IPAddress.Parse("2001:db8:1234:5678:abcd:ef01:2345:6789"),
        };

        await subscriber.HandleAsync(domainEvent, CancellationToken.None);

        IReadOnlyList<DnsQueryLogEntryEntity> entries = await WaitForEntriesAsync(
            _testHost.Host.Services
        );

        DnsQueryLogEntryEntity entry = Assert.Single(entries);
        Assert.Equal(IPAddress.Parse("2001:db8:1234:5678::"), IPAddress.Parse(entry.ClientAddress));
    }

    /// <summary>
    ///     This test proves <see cref="QueryLogPrivacyLevelEnum.FullyAnonymous" /> stores neither
    ///     the client's own address (not even a truncated subnet) nor the queried domain name --
    ///     only the fields aggregate counting needs.
    /// </summary>
    [Fact]
    public async Task HandleAsync_AtFullyAnonymousLevel_PersistsNeitherClientNorDomain()
    {
        DnsQueryLogSubscriber subscriber = new(
            _testHost.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<QueryLoggingOptions>(
                new QueryLoggingOptions
                {
                    Enable = true,
                    PrivacyLevel = QueryLogPrivacyLevelEnum.FullyAnonymous,
                }
            ),
            NullLogger<DnsQueryLogSubscriber>.Instance
        );

        await subscriber.HandleAsync(NewAnsweredEvent(), CancellationToken.None);

        IReadOnlyList<DnsQueryLogEntryEntity> entries = await WaitForEntriesAsync(
            _testHost.Host.Services
        );

        DnsQueryLogEntryEntity entry = Assert.Single(entries);
        Assert.Null(entry.ClientAddress);
        Assert.Null(entry.QueriedName);
        Assert.Equal(DnsResourceRecordTypeEnum.A, entry.QueriedType);
        Assert.Equal(DnsResponseCodeEnum.NoError, entry.ResponseCode);
        Assert.Equal(1, entry.AnswerCount);
    }

    /// <summary>
    ///     This test proves a query from a resolvable client genuinely gets its persistent
    ///     identifier stamped onto the log entry, at this scale's own default (<see cref="QueryLogPrivacyLevelEnum.Full" />)
    ///     level -- the identity-resolution half of this sub-phase's own core contract.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WithAResolvableClient_StampsClientIdentifierAndProtocolVersion()
    {
        IPAddress remoteAddress = IPAddress.Parse("198.51.100.7");
        FakeClientIdentityResolver resolver = new(
            remoteAddress,
            new ClientIdentityResolution("AA:BB:CC:DD:EE:FF", InternetProtocolVersionEnum.IPv4)
        );

        DnsQueryLogSubscriber subscriber = new(
            _testHost.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<QueryLoggingOptions>(
                new QueryLoggingOptions { Enable = true }
            ),
            NullLogger<DnsQueryLogSubscriber>.Instance,
            resolver
        );

        await subscriber.HandleAsync(NewAnsweredEvent(), CancellationToken.None);

        IReadOnlyList<DnsQueryLogEntryEntity> entries = await WaitForEntriesAsync(
            _testHost.Host.Services
        );

        DnsQueryLogEntryEntity entry = Assert.Single(entries);
        Assert.Equal("AA:BB:CC:DD:EE:FF", entry.ClientIdentifier);
        Assert.Equal(InternetProtocolVersionEnum.IPv4, entry.ClientProtocolVersion);
    }

    /// <summary>
    ///     This test proves no resolver at all (the constructor's own default, exactly like every
    ///     other test above this one in this class already constructs it) leaves both new fields
    ///     null, with no error -- the "identity-resolution infrastructure isn't wired up" case.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WithNoClientIdentityResolverConfigured_LeavesIdentityFieldsNull()
    {
        DnsQueryLogSubscriber subscriber = new(
            _testHost.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<QueryLoggingOptions>(
                new QueryLoggingOptions { Enable = true }
            ),
            NullLogger<DnsQueryLogSubscriber>.Instance
        );

        await subscriber.HandleAsync(NewAnsweredEvent(), CancellationToken.None);

        IReadOnlyList<DnsQueryLogEntryEntity> entries = await WaitForEntriesAsync(
            _testHost.Host.Services
        );

        DnsQueryLogEntryEntity entry = Assert.Single(entries);
        Assert.Null(entry.ClientIdentifier);
        Assert.Null(entry.ClientProtocolVersion);
    }

    /// <summary>
    ///     This test proves a real resolver that simply has no mapping for this particular query's
    ///     own source address (the ordinary "unknown device" case, distinct from no resolver being
    ///     configured at all) also leaves both fields null, never an exception.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenTheResolverCannotResolveThisAddress_LeavesIdentityFieldsNull()
    {
        FakeClientIdentityResolver resolver = new(
            IPAddress.Parse("203.0.113.99"), // a different address than NewAnsweredEvent() carries
            new ClientIdentityResolution("AA:BB:CC:DD:EE:FF", InternetProtocolVersionEnum.IPv4)
        );

        DnsQueryLogSubscriber subscriber = new(
            _testHost.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<QueryLoggingOptions>(
                new QueryLoggingOptions { Enable = true }
            ),
            NullLogger<DnsQueryLogSubscriber>.Instance,
            resolver
        );

        await subscriber.HandleAsync(NewAnsweredEvent(), CancellationToken.None);

        IReadOnlyList<DnsQueryLogEntryEntity> entries = await WaitForEntriesAsync(
            _testHost.Host.Services
        );

        DnsQueryLogEntryEntity entry = Assert.Single(entries);
        Assert.Null(entry.ClientIdentifier);
        Assert.Null(entry.ClientProtocolVersion);
    }

    /// <summary>
    ///     This test proves client identity is still stamped at <see cref="QueryLogPrivacyLevelEnum.DomainRedacted" />
    ///     -- the level immediately below the one that first suppresses it -- confirming the gate
    ///     genuinely sits at <see cref="QueryLogPrivacyLevelEnum.ClientAnonymized" />, not one level
    ///     earlier.
    /// </summary>
    [Fact]
    public async Task HandleAsync_AtDomainRedactedLevel_StillStampsClientIdentifier()
    {
        IPAddress remoteAddress = IPAddress.Parse("198.51.100.7");
        FakeClientIdentityResolver resolver = new(
            remoteAddress,
            new ClientIdentityResolution("AA:BB:CC:DD:EE:FF", InternetProtocolVersionEnum.IPv4)
        );

        DnsQueryLogSubscriber subscriber = new(
            _testHost.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<QueryLoggingOptions>(
                new QueryLoggingOptions
                {
                    Enable = true,
                    PrivacyLevel = QueryLogPrivacyLevelEnum.DomainRedacted,
                }
            ),
            NullLogger<DnsQueryLogSubscriber>.Instance,
            resolver
        );

        await subscriber.HandleAsync(NewAnsweredEvent(), CancellationToken.None);

        IReadOnlyList<DnsQueryLogEntryEntity> entries = await WaitForEntriesAsync(
            _testHost.Host.Services
        );

        DnsQueryLogEntryEntity entry = Assert.Single(entries);
        Assert.Equal("AA:BB:CC:DD:EE:FF", entry.ClientIdentifier);
        Assert.Equal(InternetProtocolVersionEnum.IPv4, entry.ClientProtocolVersion);
    }

    /// <summary>
    ///     This is this sub-phase's own most important privacy-gating proof:
    ///     <see cref="QueryLogPrivacyLevelEnum.ClientAnonymized" /> -- the exact level that already
    ///     starts anonymizing <see cref="DnsQueryLogEntryEntity.ClientAddress" /> (truncating it to
    ///     its own containing subnet, per <see cref="HandleAsync_AtClientAnonymizedLevel_TruncatesAnIPv4AddressToItsSubnet" />
    ///     above) -- also fully suppresses the new client identifier fields, even though a real,
    ///     matching resolver is configured and would otherwise happily resolve this exact address.
    ///     This proves suppression is a genuine privacy-level decision, not merely "happens to be
    ///     null because no resolver was configured."
    /// </summary>
    [Fact]
    public async Task HandleAsync_AtClientAnonymizedLevel_SuppressesClientIdentifierEvenWhenResolvable()
    {
        IPAddress remoteAddress = IPAddress.Parse("198.51.100.7");
        FakeClientIdentityResolver resolver = new(
            remoteAddress,
            new ClientIdentityResolution("AA:BB:CC:DD:EE:FF", InternetProtocolVersionEnum.IPv4)
        );

        DnsQueryLogSubscriber subscriber = new(
            _testHost.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<QueryLoggingOptions>(
                new QueryLoggingOptions
                {
                    Enable = true,
                    PrivacyLevel = QueryLogPrivacyLevelEnum.ClientAnonymized,
                }
            ),
            NullLogger<DnsQueryLogSubscriber>.Instance,
            resolver
        );

        await subscriber.HandleAsync(NewAnsweredEvent(), CancellationToken.None);

        IReadOnlyList<DnsQueryLogEntryEntity> entries = await WaitForEntriesAsync(
            _testHost.Host.Services
        );

        DnsQueryLogEntryEntity entry = Assert.Single(entries);
        // The address itself is still truncated (not dropped) at this level, per the existing
        // ClientAnonymized coverage above -- but the identifier, which has no equivalent
        // "truncated" middle ground, is fully suppressed.
        Assert.Equal("198.51.100.0", entry.ClientAddress);
        Assert.Null(entry.ClientIdentifier);
        Assert.Null(entry.ClientProtocolVersion);
    }

    /// <summary>
    ///     This test proves <see cref="QueryLogPrivacyLevelEnum.FullyAnonymous" /> -- strictly more
    ///     private than <see cref="QueryLogPrivacyLevelEnum.ClientAnonymized" /> -- also continues
    ///     to suppress client identity, exactly like it already suppresses the address itself.
    /// </summary>
    [Fact]
    public async Task HandleAsync_AtFullyAnonymousLevel_AlsoSuppressesClientIdentifier()
    {
        IPAddress remoteAddress = IPAddress.Parse("198.51.100.7");
        FakeClientIdentityResolver resolver = new(
            remoteAddress,
            new ClientIdentityResolution("AA:BB:CC:DD:EE:FF", InternetProtocolVersionEnum.IPv4)
        );

        DnsQueryLogSubscriber subscriber = new(
            _testHost.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<QueryLoggingOptions>(
                new QueryLoggingOptions
                {
                    Enable = true,
                    PrivacyLevel = QueryLogPrivacyLevelEnum.FullyAnonymous,
                }
            ),
            NullLogger<DnsQueryLogSubscriber>.Instance,
            resolver
        );

        await subscriber.HandleAsync(NewAnsweredEvent(), CancellationToken.None);

        IReadOnlyList<DnsQueryLogEntryEntity> entries = await WaitForEntriesAsync(
            _testHost.Host.Services
        );

        DnsQueryLogEntryEntity entry = Assert.Single(entries);
        Assert.Null(entry.ClientIdentifier);
        Assert.Null(entry.ClientProtocolVersion);
    }
}
