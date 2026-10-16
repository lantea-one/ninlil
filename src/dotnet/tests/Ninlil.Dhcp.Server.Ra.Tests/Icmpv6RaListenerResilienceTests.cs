using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Control;
using Ninlil.Dhcp.Common.Wire.Ra;
using Ninlil.Dhcp.Protocol.Ra;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra.Tests;

/// <summary>
///     This test class checks the parts of <see cref="Icmpv6RaListener" /> that need no raw
///     socket: building an advertisement without letting a provider or encoder failure escape,
///     and deciding which Router Solicitations are accepted.
/// </summary>
public sealed class Icmpv6RaListenerResilienceTests
{
    [Fact]
    public void TryBuildAdvertisements_WhenTheProviderThrows_ReturnsFalseAndLogsOneError()
    {
        SwitchableProvider provider = new() { Failure = new InvalidOperationException("Bad.") };
        ListLogger logger = new();
        Icmpv6RaListener listener = CreateListener(provider, logger);

        Assert.False(listener.TryBuildAdvertisements("eth0", out IReadOnlyList<byte[]> datagrams));
        Assert.Null(datagrams);

        // The same failure again is only a Debug entry, so a bad setting is not logged every
        // few seconds.
        Assert.False(listener.TryBuildAdvertisements("eth0", out _));
        Assert.Equal(1, logger.Count(LogLevel.Error));
        Assert.Equal(1, logger.Count(LogLevel.Debug));
    }

    [Fact]
    public void TryBuildAdvertisements_WhenTheEncoderRejectsTheMessage_ReturnsFalse_AndSucceedsOnceFixed()
    {
        SwitchableProvider provider = new()
        {
            Message = new RouterAdvertisementMessage
            {
                RouterLifetime = TimeSpan.FromSeconds(70000),
            },
        };
        ListLogger logger = new();
        Icmpv6RaListener listener = CreateListener(provider, logger);

        Assert.False(listener.TryBuildAdvertisements(null, out IReadOnlyList<byte[]> datagrams));
        Assert.Null(datagrams);
        Assert.Equal(1, logger.Count(LogLevel.Error));

        provider.Message = new RouterAdvertisementMessage
        {
            RouterLifetime = TimeSpan.FromSeconds(1800),
        };

        Assert.True(listener.TryBuildAdvertisements(null, out datagrams));
        Assert.NotEmpty(Assert.Single(datagrams));

        // After a success, a new failure is reported at Error again.
        provider.Failure = new InvalidOperationException("Bad.");
        Assert.False(listener.TryBuildAdvertisements(null, out _));
        Assert.Equal(2, logger.Count(LogLevel.Error));
    }

    [Fact]
    public void TryBuildAdvertisements_WhenTheProviderReturnsNull_ReturnsFalseWithoutLogging()
    {
        SwitchableProvider provider = new();
        ListLogger logger = new();
        Icmpv6RaListener listener = CreateListener(provider, logger);

        Assert.False(listener.TryBuildAdvertisements("eth0", out _));
        Assert.Equal(0, logger.Count(LogLevel.Error));
    }

    [Theory]
    [InlineData("fe80::1%2", false, true)]
    [InlineData("::", false, true)]
    [InlineData("::", true, false)]
    [InlineData("fe80::1%2", true, true)]
    [InlineData("::1", false, true)]
    [InlineData("2001:db8::1", false, false)]
    [InlineData("::ffff:192.0.2.1", false, false)]
    [InlineData("192.0.2.1", false, false)]
    public void IsAcceptableSolicitation_AcceptsOnlyLocalLinkSources(
        string source,
        bool withSourceLinkLayerAddress,
        bool expected
    )
    {
        RouterSolicitationMessage solicitation = new();

        if (withSourceLinkLayerAddress)
            solicitation.SetSourceLinkLayerAddress([1, 2, 3, 4, 5, 6]);

        Assert.Equal(
            expected,
            Icmpv6RaListener.IsAcceptableSolicitation(IPAddress.Parse(source), solicitation)
        );
    }

    [Fact]
    public async Task IsRunning_IsFalseWhenNotStarted_AndStopAsyncOnAStoppedListenerDoesNothing()
    {
        Icmpv6RaListener listener = CreateListener(new SwitchableProvider(), new ListLogger());

        Assert.False(((IDhcpProtocolService)listener).IsRunning);
        await listener.StopAsync();
    }

    private static Icmpv6RaListener CreateListener(
        IRaAdvertisementProvider provider,
        ILogger<Icmpv6RaListener> logger
    ) => new(provider, Options.Create(new RaProtocolOptions { Enable = true }), logger);

    /// <summary>
    ///     This class is a provider whose result the test changes between calls.
    /// </summary>
    private sealed class SwitchableProvider : IRaAdvertisementProvider
    {
        public Exception Failure { get; set; }

        public RouterAdvertisementMessage Message { get; set; }

        public RouterAdvertisementMessage BuildCurrentAdvertisement(string interfaceName = null) =>
            Failure is not null ? throw Failure : Message;
    }

    /// <summary>
    ///     This class records the level of every entry it receives.
    /// </summary>
    private sealed class ListLogger : ILogger<Icmpv6RaListener>
    {
        private readonly List<LogLevel> _levels = [];

        public int Count(LogLevel level)
        {
            lock (_levels)
                return _levels.Count(l => l == level);
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            lock (_levels)
                _levels.Add(logLevel);
        }
    }
}
