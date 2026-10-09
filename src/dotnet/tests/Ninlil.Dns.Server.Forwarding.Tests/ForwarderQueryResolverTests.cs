using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Server.Core.ClientSubnet;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Forwarding.Caching;
using Ninlil.Dns.Server.Forwarding.Clients;
using Ninlil.Dns.Server.Forwarding.Definition;
using Ninlil.Dns.Server.Forwarding.Tests.Fakes;
using Ninlil.Dns.Server.Recursive;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Validation;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This class tests <see cref="ForwarderQueryResolver" /> end to end: choosing the forwarder
///     set (global or a forwarder zone), round trips over UDP to a fake upstream on a loopback
///     port (<see cref="FakeUdpUpstream" />), the live <see cref="ForwardingOptions.Enable" />
///     switch, and DNSSEC validation of a forwarded answer with <see cref="DnssecValidator" />.
///     Zones and forwarders live in an in-memory SQLite database. The other parts of this class
///     cover caching and CNAME cloaking.
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

        // The filtering repositories are used by the CNAME-cloaking and caching tests, which run
        // a forwarded answer through the filter engine.
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
    ///     This test checks that a resolver built while <see cref="ForwardingOptions.Enable" /> is
    ///     off starts forwarding, without being rebuilt, once the flag turns on, because it reads
    ///     <c>CurrentValue</c> on every query.
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

        // A reachable forwarder is configured, so NotHandled here means "disabled", not "no
        // forwarder answered".
        DnsQueryResolution before = await resolver.TryResolveAsync(
            BuildContext(BuildQuery(7, "example.com."))
        );
        Assert.False(before.IsHandled);

        sharedOptions.Enable = true;

        // The same instance now reaches the upstream.
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
    ///     This test checks that a <see cref="ZoneTypeEnum.SecondaryForwarder" /> zone's
    ///     <see cref="ZoneEntity.ForwarderAddresses" /> are used like a
    ///     <see cref="ZoneTypeEnum.Forwarder" /> zone's. In a deployment they come from a zone
    ///     transfer (covered by
    ///     <c>Ninlil.Dns.Server.Authoritative.Tests.Forwarding.SecondaryForwarderZoneTransferTests</c>);
    ///     here they are seeded directly.
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

    /// <summary>
    ///     This test checks the whole DNSSEC path of a forwarded answer with the real validator
    ///     over a signed root, <c>com</c> and <c>example.com</c> (see the Dnssec part of this
    ///     class): a genuine answer is accepted and the same answer with a damaged signature is
    ///     not.
    /// </summary>
    [Fact]
    public async Task TryResolveAsync_ReValidatesDnssec_AcceptingSecureAndRejectingBogus()
    {
        DnssecEnvironment secure = new();
        ForwarderQueryResolver secureResolver = await CreateDnssecResolverAsync(secure);

        DnsQueryResolution secureResolution = await secureResolver.TryResolveAsync(
            BuildContext(BuildClientQuery("www.example.com"))
        );

        Assert.True(secureResolution.IsHandled);

        // The only configured forwarder returns a damaged signature. It counts as a failed
        // attempt, leaving nothing to try, so the result is NotHandled.
        await using NinlilDnsContext resetContext = _provider
            .GetRequiredService<IServiceScopeFactory>()
            .CreateScope()
            .ServiceProvider.GetRequiredService<NinlilDnsContext>();
        resetContext.RemoveRange(resetContext.Set<DnsForwarderEntity>());
        await resetContext.SaveChangesAsync();

        DnssecEnvironment bogus = new();
        bogus.Upstream.Tamper = message => CorruptSignature(message, A);
        ForwarderQueryResolver bogusResolver = await CreateDnssecResolverAsync(bogus);

        DnsQueryResolution bogusResolution = await bogusResolver.TryResolveAsync(
            BuildContext(BuildClientQuery("www.example.com", dnssecOk: true))
        );

        Assert.False(bogusResolution.IsHandled);
    }

    private ForwarderQueryResolver CreateResolver(
        ForwardingOptions forwardingOptions = null,
        RecursiveServerOptions recursiveOptions = null,
        IDnssecValidator validator = null,
        IForwardedResponseCache responseCache = null,
        IReadOnlyList<IUpstreamForwarderClient> forwarderClients = null,
        EdnsClientSubnetService clientSubnetService = null,
        IOptionsMonitor<RecursiveServerOptions> recursiveMonitor = null,
        InsecureDomainPolicy insecureDomains = null
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
            forwarderClients
            ??
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
            recursiveMonitor
                ?? new StaticOptionsMonitor<RecursiveServerOptions>(
                    recursiveOptions
                        ?? new RecursiveServerOptions { EnableDnssecValidation = false }
                ),
            NullLogger<ForwarderQueryResolver>.Instance,
            clientSubnetService,
            responseCache,
            insecureDomains
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
    ///     <see cref="ZoneTypeEnum.SecondaryForwarder" /> zone with its
    ///     <see cref="ZoneEntity.ForwarderAddresses" /> already filled in, in place of the zone
    ///     transfer a secondary forwarder zone gets them from.
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

    /// <summary>
    ///     This class always reports Insecure. It is the validator for tests that do not exercise
    ///     DNSSEC, which leave <see cref="RecursiveServerOptions.EnableDnssecValidation" /> off.
    /// </summary>
    private sealed class AlwaysInsecureDnssecValidatorStub : IDnssecValidator
    {
        public Task<DnssecValidationResult> ValidateAsync(
            DnsName queriedName,
            DnsResourceRecordTypeEnum queriedType,
            DnsClassEnum recordClass,
            IterativeResolutionResult result,
            QueryBudget budget,
            CancellationToken stoppingToken = default,
            IDnssecRecordFetcher fetcher = null
        ) => Task.FromResult(DnssecValidationResult.Insecure());
    }

    /// <summary>
    ///     This test checks that a signed client query reaches the upstream without its TSIG record:
    ///     the signature belongs to the client-to-server hop. The client's own request keeps it,
    ///     because the pipeline signs the reply from it.
    /// </summary>
    [Fact]
    public async Task TryResolveAsync_WithASignedClientQuery_ForwardsItWithoutTheTsigRecord()
    {
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.10"))
        );
        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");
        ForwarderQueryResolver resolver = CreateResolver();

        DnsMessage request = BuildQuery(43, "example.com.");
        request.Tsig = new DnsTsigOptions(
            DnsName.Parse("client-key.example."),
            DnsTsigOptions.HmacSha256AlgorithmName,
            1,
            300,
            new byte[32],
            43,
            DnsResponseCodeEnum.NoError,
            []
        );

        DnsQueryResolution resolution = await resolver.TryResolveAsync(BuildContext(request));

        Assert.True(resolution.IsHandled);
        Assert.NotNull(upstream.LastQuery);
        DnsMessage forwarded = DnsMessageReader.Read(upstream.LastQuery);
        Assert.Null(forwarded.Tsig);
        Assert.Equal(request.Questions[0].Name, forwarded.Questions[0].Name);
        Assert.NotNull(request.Tsig);
    }

    /// <summary>
    ///     This class is a minimal fake upstream DNS server: a UDP socket on an ephemeral loopback
    ///     port that answers every datagram with the same canned response, whatever was asked.
    ///     That is enough to exercise <see cref="PlainForwarderClient" /> and
    ///     <see cref="ForwarderQueryResolver" /> over a socket without a listener and pipeline.
    /// </summary>
    private sealed class FakeUdpUpstream : IDisposable
    {
        private readonly Socket _socket;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        private int _queryCount;
        private byte[] _lastQuery;

        private FakeUdpUpstream(Socket socket, byte[] responseBytes)
        {
            _socket = socket;
            _loop = RunAsync(responseBytes, _cts.Token);
        }

        public int Port => ((IPEndPoint)_socket.LocalEndPoint).Port;

        /// <summary>
        ///     This property contains how many queries this upstream has received, which the
        ///     cache tests use to show a cached answer never went upstream.
        /// </summary>
        public int QueryCount => Volatile.Read(ref _queryCount);

        /// <summary>
        ///     This property contains the bytes of the latest query received, or
        ///     <see langword="null" /> before the first one.
        /// </summary>
        public byte[] LastQuery => Volatile.Read(ref _lastQuery);

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

                    Volatile.Write(ref _lastQuery, buffer[..received.ReceivedBytes]);
                    Interlocked.Increment(ref _queryCount);

                    // Copy the query's transaction ID (the header's first two octets, RFC 1035
                    // §4.1.1) into the canned response, as a server does; PlainForwarderClient
                    // discards answers whose ID does not match.
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
