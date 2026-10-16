using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire.Ra;
using Ninlil.Dhcp.Protocol.Ra;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra.Tests;

/// <summary>
///     This test class checks how <see cref="Icmpv6RaListener" /> keeps every Router
///     Advertisement within 1240 octets: a message within the limit is one datagram, a larger
///     one is split into parts that each repeat the header fields and the Source Link-Layer
///     Address and MTU options, and an option that cannot fit alone is dropped. It uses a fixed
///     provider, a recording sender and a manual clock, so no raw socket is involved.
/// </summary>
public sealed class RaAdvertisementSplitTests
{
    private static readonly byte[] LinkLayerAddress = [0x02, 0, 0, 0, 0, 0x01];

    [Fact]
    public async Task Pass_WithAnAdvertisementOverTheLimit_SendsEveryPartWithinTheLimit()
    {
        ManualTimeProvider time = new();
        RecordingSender sender = new();
        Icmpv6RaListener listener = CreateListener(
            time,
            new FixedProvider(BuildMessage(prefixCount: 40))
        );

        await listener.RunUnsolicitedPassAsync(sender, [], CancellationToken.None);

        Assert.True(sender.Datagrams.Count >= 2);
        Assert.All(
            sender.Datagrams,
            datagram => Assert.True(datagram.Length <= Icmpv6RaListener.MaximumAdvertisementBytes)
        );
        Assert.All(sender.Datagrams, datagram => Assert.Equal(2, datagram.InterfaceIndex));
    }

    [Fact]
    public async Task Pass_WithTwoInterfacesAndAnOversizedAdvertisement_SplitsOnEachWithOneReservationPerInterface()
    {
        ManualTimeProvider time = new();
        RecordingSender sender = new();
        Icmpv6RaListener listener = CreateListener(
            time,
            new FixedProvider(BuildMessage(prefixCount: 40))
        );
        listener.InterfaceEnumerator = () => [(2, "eth0"), (3, "eth1")];

        await listener.RunUnsolicitedPassAsync(sender, [], CancellationToken.None);

        // Every part of every interface went out in this one pass, so the window was taken once
        // per advertisement: a reservation per part would refuse the second part.
        Assert.Equal(
            sender.Datagrams.Count(datagram => datagram.InterfaceIndex == 2),
            sender.Datagrams.Count(datagram => datagram.InterfaceIndex == 3)
        );
        Assert.True(sender.Datagrams.Count(datagram => datagram.InterfaceIndex == 2) >= 2);
        Assert.False(listener.SendWindow.TryReserveMulticastNow(2, out _));
    }

    [Fact]
    public void TryBuildAdvertisements_WithFortyPrefixes_SplitsIntoPartsThatRepeatTheHeaderAndKeepEveryPrefixOnceInOrder()
    {
        RouterAdvertisementMessage message = BuildMessage(prefixCount: 40);
        Icmpv6RaListener listener = CreateListener(
            new ManualTimeProvider(),
            new FixedProvider(message)
        );

        Assert.True(listener.TryBuildAdvertisements("eth0", out IReadOnlyList<byte[]> datagrams));

        Assert.True(datagrams.Count >= 2);
        List<IPAddress> prefixes = [];

        foreach (byte[] datagram in datagrams)
        {
            Assert.True(datagram.Length <= Icmpv6RaListener.MaximumAdvertisementBytes);
            Assert.True(RaMessageReader.TryReadRouterAdvertisement(datagram, out var part, out _));
            Assert.Equal(64, part.CurrentHopLimit);
            Assert.True(part.ManagedAddressConfiguration);
            Assert.True(part.OtherConfiguration);
            Assert.Equal(TimeSpan.FromSeconds(1800), part.RouterLifetime);
            Assert.Equal(TimeSpan.FromSeconds(30), part.ReachableTime);
            Assert.Equal(TimeSpan.FromSeconds(1), part.RetransTimer);
            Assert.True(part.TryGetSourceLinkLayerAddress(out byte[] linkLayerAddress));
            Assert.Equal(LinkLayerAddress, linkLayerAddress);
            Assert.True(part.TryGetMtu(out uint mtu));
            Assert.Equal(1500u, mtu);

            Assert.True(part.TryGetPrefixInformationOptions(out var partPrefixes));
            prefixes.AddRange(partPrefixes.Select(prefix => prefix.Prefix));
        }

        Assert.Equal(
            Enumerable.Range(1, 40).Select(index => IPAddress.Parse($"2001:db8:{index:x}::")),
            prefixes
        );
    }

    [Fact]
    public void TryBuildAdvertisements_WithAMessageAtTheLimit_ReturnsOneDatagramIdenticalToTheWriterOutput()
    {
        // 16 header + 8 SLLA + 8 MTU + 37 * 32 prefixes + 24 RDNSS = 1240 exactly.
        RouterAdvertisementMessage message = BuildMessage(prefixCount: 37);
        message.SetRecursiveDnsServers(TimeSpan.FromMinutes(30), [IPAddress.Parse("2001:db8::53")]);
        byte[] expected = RaMessageWriter.WriteRouterAdvertisement(message);
        Assert.Equal(Icmpv6RaListener.MaximumAdvertisementBytes, expected.Length);
        Icmpv6RaListener listener = CreateListener(
            new ManualTimeProvider(),
            new FixedProvider(message)
        );

        Assert.True(listener.TryBuildAdvertisements("eth0", out IReadOnlyList<byte[]> datagrams));

        Assert.Equal(expected, Assert.Single(datagrams));
    }

    [Fact]
    public void TryBuildAdvertisements_WithAMessageOneOptionOverTheLimit_SplitsIt()
    {
        RouterAdvertisementMessage message = BuildMessage(prefixCount: 37);
        message.SetRecursiveDnsServers(TimeSpan.FromMinutes(30), [IPAddress.Parse("2001:db8::53")]);
        message.AddPrefixInformation(
            new RaPrefixInformationOption
            {
                PrefixLength = 64,
                Prefix = IPAddress.Parse("2001:db8:ff::"),
            }
        );
        Icmpv6RaListener listener = CreateListener(
            new ManualTimeProvider(),
            new FixedProvider(message)
        );

        Assert.True(listener.TryBuildAdvertisements("eth0", out IReadOnlyList<byte[]> datagrams));

        Assert.Equal(2, datagrams.Count);
        Assert.All(
            datagrams,
            datagram => Assert.True(datagram.Length <= Icmpv6RaListener.MaximumAdvertisementBytes)
        );
    }

    [Fact]
    public void TryBuildAdvertisements_WithAnOptionThatCannotFitAlone_DropsItAndReportsOneError()
    {
        // 100 RDNSS addresses make one 1608-octet option, which no datagram can carry.
        RouterAdvertisementMessage message = BuildMessage(prefixCount: 2);
        message.SetRecursiveDnsServers(
            TimeSpan.FromMinutes(30),
            [.. Enumerable.Range(1, 100).Select(index => IPAddress.Parse($"2001:db8::{index:x}"))]
        );
        ListLogger logger = new();
        Icmpv6RaListener listener = CreateListener(
            new ManualTimeProvider(),
            new FixedProvider(message),
            logger
        );

        Assert.True(listener.TryBuildAdvertisements("eth0", out IReadOnlyList<byte[]> datagrams));
        Assert.True(listener.TryBuildAdvertisements("eth0", out _));

        byte[] datagram = Assert.Single(datagrams);
        Assert.True(RaMessageReader.TryReadRouterAdvertisement(datagram, out var part, out _));
        Assert.False(part.TryGetRecursiveDnsServers(out _, out _));
        Assert.True(part.TryGetPrefixInformationOptions(out var prefixes));
        Assert.Equal(2, prefixes.Count);
        Assert.Equal(1, logger.Count(LogLevel.Error));
        Assert.Equal(1, logger.Count(LogLevel.Debug));
    }

    /// <summary>
    ///     This method builds an advertisement with header fields, a Source Link-Layer Address, an
    ///     MTU and the given number of 32-octet Prefix Information options, in that order.
    /// </summary>
    private static RouterAdvertisementMessage BuildMessage(int prefixCount)
    {
        RouterAdvertisementMessage message = new()
        {
            CurrentHopLimit = 64,
            ManagedAddressConfiguration = true,
            OtherConfiguration = true,
            RouterLifetime = TimeSpan.FromSeconds(1800),
            ReachableTime = TimeSpan.FromSeconds(30),
            RetransTimer = TimeSpan.FromSeconds(1),
        };
        message.SetSourceLinkLayerAddress(LinkLayerAddress);
        message.SetMtu(1500);

        for (int index = 0; index < prefixCount; index++)
            message.AddPrefixInformation(
                new RaPrefixInformationOption
                {
                    PrefixLength = 64,
                    OnLink = true,
                    Autonomous = true,
                    ValidLifetime = TimeSpan.FromDays(30),
                    PreferredLifetime = TimeSpan.FromDays(7),
                    Prefix = IPAddress.Parse($"2001:db8:{index + 1:x}::"),
                }
            );

        return message;
    }

    /// <summary>
    ///     This method builds a listener on the manual clock with a single interface, index 2,
    ///     that logs to <paramref name="logger" /> when one is given.
    /// </summary>
    private static Icmpv6RaListener CreateListener(
        ManualTimeProvider time,
        IRaAdvertisementProvider provider,
        ILogger<Icmpv6RaListener> logger = null
    ) =>
        new(
            provider,
            Options.Create(new RaProtocolOptions { Enable = true }),
            logger ?? NullLogger<Icmpv6RaListener>.Instance,
            time
        )
        {
            InterfaceEnumerator = () => [(2, "eth0")],
        };

    /// <summary>
    ///     This class is a logger that counts the entries of each level.
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

    /// <summary>
    ///     This class is a provider that returns the same message for every interface.
    /// </summary>
    private sealed class FixedProvider(RouterAdvertisementMessage message)
        : IRaAdvertisementProvider
    {
        public RouterAdvertisementMessage BuildCurrentAdvertisement(string interfaceName = null) =>
            message;
    }

    /// <summary>
    ///     This class is a sender that keeps every datagram with its outgoing interface, instead
    ///     of using a socket.
    /// </summary>
    private sealed class RecordingSender : IRaDatagramSender
    {
        private readonly List<SentDatagram> _datagrams = [];

        public List<SentDatagram> Datagrams
        {
            get
            {
                lock (_datagrams)
                    return _datagrams.ToList();
            }
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> datagram,
            IPAddress destination,
            int? interfaceIndex,
            CancellationToken stoppingToken
        )
        {
            lock (_datagrams)
                _datagrams.Add(new SentDatagram(datagram.ToArray(), interfaceIndex));

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     This record is one datagram a <see cref="RecordingSender" /> received.
    /// </summary>
    private sealed record SentDatagram(byte[] Bytes, int? InterfaceIndex)
    {
        public int Length => Bytes.Length;
    }
}
