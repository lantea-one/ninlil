using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Server.Core.Identity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class covers the 85-byte client identifier limit of both request handlers: a
///     DHCPv4 Client-identifier option or a DHCPv6 DUID over the limit gets no reply and leaves
///     no lease row, and the refusal log is rate limited to one Warning per minute. It uses the
///     real engine on the temporary SQLite hosts (<see cref="Dhcpv4EngineTestHost" /> and
///     <see cref="Dhcpv6EngineTestHost" />) and calls the handlers directly. The log tests build
///     the handler by hand with a <see cref="ManualTimeProvider" /> and a recording logger that
///     keeps Debug entries too, which the hosts' <see cref="CapturingLoggerProvider" /> does not.
/// </summary>
public sealed class LongClientIdentifierTests : IAsyncLifetime
{
    private static readonly IPEndPoint RemoteEndPoint = new(IPAddress.Loopback, 68);

    private Dhcpv4EngineTestHost _v4Host;
    private Dhcpv6EngineTestHost _v6Host;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _v4Host = new Dhcpv4EngineTestHost();
        await _v4Host.InitializeAsync();
        _v6Host = new Dhcpv6EngineTestHost();
        await _v6Host.InitializeAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _v4Host.DisposeAsync();
        await _v6Host.DisposeAsync();
    }

    [Fact]
    public async Task Discover_WithAnIdentifierOf86Bytes_GetsNoOfferAndLeavesNoLease()
    {
        await _v4Host.SeedScopeAsync("long-v4", "192.0.2.10", "192.0.2.12");

        Dhcpv4Message offer = await _v4Host.Handler.HandleRequestAsync(
            NewDiscover(86),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.Null(offer);
        Assert.Equal(0, await CountLeasesAsync(_v4Host.Host.Services));
    }

    [Fact]
    public async Task Discover_WithAnIdentifierOf85Bytes_IsOffered()
    {
        await _v4Host.SeedScopeAsync("limit-v4", "192.0.2.10", "192.0.2.12");

        Dhcpv4Message offer = await _v4Host.Handler.HandleRequestAsync(
            NewDiscover(85),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.Equal(1, await CountLeasesAsync(_v4Host.Host.Services));
    }

    [Fact]
    public async Task Solicit_WithADuidOf90Bytes_GetsNoReplyAndLeavesNoLease()
    {
        await _v6Host.SeedAddressScopeAsync("long-v6", "2001:db8:6::10", "2001:db8:6::20");

        Dhcpv6Message reply = await _v6Host.Handler.HandleRequestAsync(
            NewSolicit(90),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.Null(reply);
        Assert.Equal(0, await CountLeasesAsync(_v6Host.Host.Services));
    }

    [Fact]
    public async Task Solicit_WithADuidOf85Bytes_IsAdvertised()
    {
        await _v6Host.SeedAddressScopeAsync("limit-v6", "2001:db8:6::10", "2001:db8:6::20");

        Dhcpv6Message reply = await _v6Host.Handler.HandleRequestAsync(
            NewSolicit(85),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(reply);
        Assert.Equal(Dhcpv6MessageTypeEnum.Advertise, reply.MessageType);
    }

    [Fact]
    public async Task Discover_RefusedThreeTimesInAMinute_LogsOneWarningAndTwoDebugEntries()
    {
        ManualTimeProvider time = new();
        RecordingLogger<Dhcpv4RequestHandler> logger = new();
        Dhcpv4RequestHandler handler = NewV4Handler(logger, time);

        for (int i = 0; i < 3; i++)
            Assert.Null(await handler.HandleRequestAsync(NewDiscover(86), RemoteEndPoint, default));

        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal(2, logger.Entries.Count(entry => entry.Level == LogLevel.Debug));
        Assert.Contains("1 DHCPv4 request(s)", logger.Entries[0].Message);

        // A minute later the next refusal warns again and reports the three refused since.
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(await handler.HandleRequestAsync(NewDiscover(86), RemoteEndPoint, default));

        Assert.Equal(2, logger.Entries.Count(entry => entry.Level == LogLevel.Warning));
        LogEntry second = logger.Entries.Last(entry => entry.Level == LogLevel.Warning);
        Assert.Contains("3 DHCPv4 request(s)", second.Message);
    }

    [Fact]
    public async Task Solicit_RefusedThreeTimesInAMinute_LogsOneWarningAndTwoDebugEntries()
    {
        ManualTimeProvider time = new();
        RecordingLogger<Dhcpv6RequestHandler> logger = new();
        Dhcpv6RequestHandler handler = NewV6Handler(logger, time);

        for (int i = 0; i < 3; i++)
            Assert.Null(await handler.HandleRequestAsync(NewSolicit(90), RemoteEndPoint, default));

        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal(2, logger.Entries.Count(entry => entry.Level == LogLevel.Debug));

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(await handler.HandleRequestAsync(NewSolicit(90), RemoteEndPoint, default));

        LogEntry second = logger.Entries.Last(entry => entry.Level == LogLevel.Warning);
        Assert.Contains("3 DHCPv6 request(s)", second.Message);
    }

    /// <summary>
    ///     This method builds a DHCPDISCOVER whose Client-identifier option is the given number
    ///     of bytes.
    /// </summary>
    private static Dhcpv4Message NewDiscover(int identifierLength)
    {
        byte[] identifier = new byte[identifierLength];
        Array.Fill(identifier, (byte)0x42);
        identifier[0] = 0x01;

        Dhcpv4Message discover = new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            TransactionId = 0x5000,
            HardwareAddressLength = 6,
            ClientHardwareAddress = [0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0x77],
        };
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        discover.SetClientIdentifier(identifier);
        return discover;
    }

    /// <summary>
    ///     This method builds a SOLICIT with an IA_NA whose DUID is the given number of bytes.
    /// </summary>
    private static Dhcpv6Message NewSolicit(int duidLength)
    {
        byte[] duid = new byte[duidLength];
        Array.Fill(duid, (byte)0x42);
        duid[0] = 0x00;
        duid[1] = 0x03;

        Dhcpv6Message solicit = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.Solicit,
            TransactionId = 0x5000,
        };
        solicit.SetClientIdentifier(duid);
        solicit.SetElapsedTime(TimeSpan.Zero);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(
            new Dhcpv6IdentityAssociationForNonTemporaryAddresses { IdentityAssociationId = 1 }
        );
        return solicit;
    }

    /// <summary>
    ///     This method counts the lease rows in a host's database, in a separate scope so it sees
    ///     what the engine saved.
    /// </summary>
    private static async Task<int> CountLeasesAsync(IServiceProvider services)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ILeaseRepository>().CountAsync();
    }

    /// <summary>
    ///     This method builds a DHCPv4 handler on the v4 host's services with the given logger and
    ///     clock.
    /// </summary>
    private Dhcpv4RequestHandler NewV4Handler(
        ILogger<Dhcpv4RequestHandler> logger,
        TimeProvider time
    ) =>
        new(
            _v4Host.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            _v4Host.Host.Services.GetRequiredService<IDhcpServerIdentifierProvider>(),
            logger,
            time
        );

    /// <summary>
    ///     This method builds a DHCPv6 handler on the v6 host's services with the given logger and
    ///     clock.
    /// </summary>
    private Dhcpv6RequestHandler NewV6Handler(
        ILogger<Dhcpv6RequestHandler> logger,
        TimeProvider time
    ) =>
        new(
            _v6Host.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            _v6Host.Host.Services.GetRequiredService<IDhcpv6ServerIdentifierProvider>(),
            _v6Host.Host.Services.GetRequiredService<IOptions<DhcpServerOptions>>(),
            logger,
            time
        );

    /// <summary>
    ///     This type is one entry a <see cref="RecordingLogger{T}" /> captured.
    /// </summary>
    /// <param name="Level">The log level.</param>
    /// <param name="Message">The formatted message.</param>
    private sealed record LogEntry(LogLevel Level, string Message);

    /// <summary>
    ///     This class is a logger that records every entry at every level, so the tests can count
    ///     Warning and Debug entries of one handler.
    /// </summary>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly object _lock = new();
        private readonly List<LogEntry> _entries = [];

        /// <summary>
        ///     This property contains a snapshot of the entries logged so far.
        /// </summary>
        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_lock)
                    return [.. _entries];
            }
        }

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

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
            lock (_lock)
                _entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }
}
