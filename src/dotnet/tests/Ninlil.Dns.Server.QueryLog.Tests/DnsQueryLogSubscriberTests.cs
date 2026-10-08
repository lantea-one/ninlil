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
///     This class tests <see cref="DnsQueryLogSubscriber" />: that an answered event becomes a
///     correctly mapped row, that nothing is written when logging is disabled, what each privacy
///     level stores, and when client identity is stored. The subscriber is constructed directly
///     against <see cref="QueryLogTestHost" /> (SQLite), with a fixed options monitor and, where
///     needed, a fake identity resolver.
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
    ///     This method polls for up to about a second until the log has entries.
    ///     <see cref="DnsQueryLogSubscriber.HandleAsync" /> starts the write in the background and
    ///     returns at once, so the test has to wait for it.
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

        // A disabled subscriber starts no write. A poll-until-non-empty loop cannot show that
        // nothing happens, so wait a fixed time and check the log is still empty.
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
    ///     This test checks that <see cref="QueryLogPrivacyLevelEnum.DomainRedacted" /> stores no
    ///     queried name, while the other fields, including the client address and the queried
    ///     type, are stored as received, so statistics by type and response code still work.
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
    ///     This test checks that <see cref="QueryLogPrivacyLevelEnum.ClientAnonymized" /> stores an
    ///     IPv4 client address truncated to its /24 (last octet zeroed), and also drops the domain
    ///     name as <see cref="QueryLogPrivacyLevelEnum.DomainRedacted" /> does.
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
    ///     This test checks that an IPv4-mapped IPv6 client address (what a dual-stack socket
    ///     reports for an IPv4 client) is truncated as IPv4, not as IPv6, which would have logged
    ///     <c>::</c> for every IPv4 client.
    /// </summary>
    [Fact]
    public async Task HandleAsync_AtClientAnonymizedLevel_TruncatesAnIPv4MappedAddressAsIPv4()
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

        await subscriber.HandleAsync(
            NewAnsweredEvent() with
            {
                RemoteAddress = IPAddress.Parse("::ffff:192.0.2.9"),
            },
            CancellationToken.None
        );

        IReadOnlyList<DnsQueryLogEntryEntity> entries = await WaitForEntriesAsync(
            _testHost.Host.Services
        );

        Assert.Equal("192.0.2.0", Assert.Single(entries).ClientAddress);
    }

    /// <summary>
    ///     This test checks the same truncation for an IPv6 client address: the last 64 bits (the
    ///     last four groups) are zeroed, leaving the /64 prefix.
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
    ///     This test checks that <see cref="QueryLogPrivacyLevelEnum.FullyAnonymous" /> stores
    ///     neither the client address (not even its subnet) nor the queried name, only the fields
    ///     statistics need.
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
    ///     This test checks that a query from a resolvable client gets its persistent identifier
    ///     and protocol version stored, at the default
    ///     <see cref="QueryLogPrivacyLevelEnum.Full" /> level.
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
    ///     This test checks that without a resolver (the constructor default) both identity fields
    ///     stay null, with no error.
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
    ///     This test checks that a resolver with no mapping for the source address (an unknown
    ///     device, as opposed to no resolver) also leaves both fields null, with no error.
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
    ///     This test checks that client identity is still stored at
    ///     <see cref="QueryLogPrivacyLevelEnum.DomainRedacted" />, the level just below the one that
    ///     drops it, so the cut-off is at <see cref="QueryLogPrivacyLevelEnum.ClientAnonymized" />
    ///     and not one level earlier.
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
    ///     This test checks that <see cref="QueryLogPrivacyLevelEnum.ClientAnonymized" />, the level
    ///     that starts truncating <see cref="DnsQueryLogEntryEntity.ClientAddress" /> (see
    ///     <see cref="HandleAsync_AtClientAnonymizedLevel_TruncatesAnIPv4AddressToItsSubnet" />),
    ///     drops the client identity fields even though a matching resolver is configured. The
    ///     fields are null because of the privacy level, not a missing resolver.
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
        // The address is truncated, not dropped, at this level, but the identifier has no
        // truncated form, so it is dropped.
        Assert.Equal("198.51.100.0", entry.ClientAddress);
        Assert.Null(entry.ClientIdentifier);
        Assert.Null(entry.ClientProtocolVersion);
    }

    /// <summary>
    ///     This test checks that <see cref="QueryLogPrivacyLevelEnum.FullyAnonymous" /> also drops
    ///     client identity, as it drops the address.
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
