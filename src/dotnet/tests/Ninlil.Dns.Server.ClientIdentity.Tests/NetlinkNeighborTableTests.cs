using System.Buffers.Binary;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.ClientIdentity.Presence;
using Ninlil.Dns.Server.ClientIdentity.Presence.Netlink;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Tests;

/// <summary>
///     This class covers the rtnetlink neighbor-table path: <see cref="NetlinkNeighborMessageParser" />
///     against constructed datagrams, <see cref="NetlinkNeighborTableReader" /> against the test
///     machine's kernel, and the filtering in <see cref="NeighborTablePresenceService" /> of which
///     entries count as a sighting (with a fake reader and a recording recorder).
/// </summary>
public class NetlinkNeighborTableTests
{
    /// <summary>The kernel's STALE neighbor state bit.</summary>
    private const ushort NudStale = 0x04;

    /// <summary>The kernel's DELAY neighbor state bit.</summary>
    private const ushort NudDelay = 0x08;

    /// <summary>A sample MAC address.</summary>
    private static readonly byte[] Mac = [0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0x01];

    /// <summary>
    ///     This method builds one <c>RTM_NEWNEIGH</c> message with <c>NDA_DST</c> and, optionally,
    ///     <c>NDA_LLADDR</c> attributes, laid out per <c>rtnetlink(7)</c>.
    /// </summary>
    private static byte[] NeighborMessage(IPAddress address, byte[] mac, ushort state)
    {
        byte[] destination = address.GetAddressBytes();
        List<byte> payload = [];

        // ndmsg: family, 3 pad octets, ifindex, state, flags, type.
        byte[] ndmsg = new byte[12];
        ndmsg[0] = (byte)(
            address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 2 : 10
        );
        BinaryPrimitives.WriteInt32LittleEndian(ndmsg.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(ndmsg.AsSpan(8), state);
        payload.AddRange(ndmsg);
        AppendAttribute(payload, 1, destination);
        if (mac is not null)
            AppendAttribute(payload, 2, mac);

        byte[] message = new byte[16 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(message, (uint)message.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(4), 28);
        payload.CopyTo(message, 16);
        return message;
    }

    /// <summary>
    ///     This method appends one 4-octet-aligned <c>rtattr</c>.
    /// </summary>
    private static void AppendAttribute(List<byte> payload, ushort type, byte[] value)
    {
        byte[] header = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)(4 + value.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), type);
        payload.AddRange(header);
        payload.AddRange(value);
        while (payload.Count % 4 != 0)
            payload.Add(0);
    }

    /// <summary>
    ///     This method builds an <c>NLMSG_DONE</c> message.
    /// </summary>
    private static byte[] DoneMessage()
    {
        byte[] message = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(message, 20);
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(4), 3);
        return message;
    }

    /// <summary>
    ///     This test checks that IPv4 and IPv6 entries, their MACs and states, all parse from one
    ///     datagram, and that a datagram without <c>NLMSG_DONE</c> is reported as unfinished.
    /// </summary>
    [Fact]
    public void ParsesIPv4AndIPv6EntriesAcrossADatagram()
    {
        byte[] datagram =
        [
            .. NeighborMessage(
                IPAddress.Parse("192.0.2.10"),
                Mac,
                NetlinkNeighborMessageParser.NudReachable
            ),
            .. NeighborMessage(IPAddress.Parse("fe80::1"), Mac, NudStale),
        ];
        List<NeighborTableEntry> entries = [];

        NetlinkDumpStatus status = NetlinkNeighborMessageParser.ParseDatagram(datagram, entries);

        Assert.Equal(NetlinkDumpStatus.Continue, status);
        Assert.Equal(2, entries.Count);
        Assert.Equal(IPAddress.Parse("192.0.2.10"), entries[0].Address);
        Assert.Equal(Mac, entries[0].LinkLayerAddress);
        Assert.True(entries[0].IsConfirmedReachable);
        Assert.Equal(IPAddress.Parse("fe80::1"), entries[1].Address);
        Assert.False(entries[1].IsConfirmedReachable);
    }

    /// <summary>
    ///     This test checks that <c>NLMSG_DONE</c> ends the dump, and that an unresolved entry (no
    ///     <c>NDA_LLADDR</c>) still parses, with no link-layer address.
    /// </summary>
    [Fact]
    public void DoneEndsTheDumpAndAnUnresolvedEntryHasNoMac()
    {
        byte[] datagram =
        [
            .. NeighborMessage(IPAddress.Parse("192.0.2.11"), null, 0x01),
            .. DoneMessage(),
        ];
        List<NeighborTableEntry> entries = [];

        Assert.Equal(
            NetlinkDumpStatus.Complete,
            NetlinkNeighborMessageParser.ParseDatagram(datagram, entries)
        );
        Assert.Null(Assert.Single(entries).LinkLayerAddress);
    }

    /// <summary>
    ///     This test checks that a truncated message fails the dump rather than misreading past it.
    /// </summary>
    [Fact]
    public void ATruncatedMessageEndsParsingSafely()
    {
        byte[] message = NeighborMessage(
            IPAddress.Parse("192.0.2.12"),
            Mac,
            NetlinkNeighborMessageParser.NudReachable
        );
        List<NeighborTableEntry> entries = [];

        Assert.Equal(
            NetlinkDumpStatus.Failed,
            NetlinkNeighborMessageParser.ParseDatagram(
                message.AsSpan(0, message.Length - 4),
                entries
            )
        );
        Assert.Empty(entries);
    }

    /// <summary>
    ///     This method builds an <c>NLMSG_ERROR</c> message carrying the given error number.
    /// </summary>
    private static byte[] ErrorMessage(int error)
    {
        // nlmsghdr, the s32 error, then the echoed request header (nlmsgerr).
        byte[] message = new byte[36];
        BinaryPrimitives.WriteUInt32LittleEndian(message, 36);
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(16), error);
        return message;
    }

    /// <summary>
    ///     This test checks that an <c>NLMSG_ERROR</c> with a non-zero error after two neighbor
    ///     messages fails the dump, so the caller discards the two entries.
    /// </summary>
    [Fact]
    public void AnErrorMessageAfterEntries_FailsTheDump()
    {
        byte[] datagram =
        [
            .. NeighborMessage(IPAddress.Parse("192.0.2.20"), Mac, NudStale),
            .. NeighborMessage(IPAddress.Parse("192.0.2.21"), Mac, NudStale),
            .. ErrorMessage(-16),
        ];
        List<NeighborTableEntry> entries = [];

        NetlinkDumpStatus status = NetlinkNeighborMessageParser.ParseDatagram(datagram, entries);

        Assert.Equal(NetlinkDumpStatus.Failed, status);
    }

    /// <summary>
    ///     This test checks that a length field running past the datagram fails the dump.
    /// </summary>
    [Fact]
    public void ALengthThatOverrunsTheDatagram_FailsTheDump()
    {
        byte[] message = NeighborMessage(IPAddress.Parse("192.0.2.22"), Mac, NudStale);
        BinaryPrimitives.WriteUInt32LittleEndian(message, (uint)message.Length + 8);

        Assert.Equal(
            NetlinkDumpStatus.Failed,
            NetlinkNeighborMessageParser.ParseDatagram(message, [])
        );
    }

    /// <summary>
    ///     This test checks that a message flagged <c>NLM_F_DUMP_INTR</c> (the table changed during
    ///     the dump) fails the dump.
    /// </summary>
    [Fact]
    public void AMessageFlaggedDumpInterrupted_FailsTheDump()
    {
        byte[] message = NeighborMessage(IPAddress.Parse("192.0.2.23"), Mac, NudStale);
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(6), 0x02 | 0x10);

        Assert.Equal(
            NetlinkDumpStatus.Failed,
            NetlinkNeighborMessageParser.ParseDatagram(message, [])
        );
    }

    /// <summary>
    ///     This test checks that an <c>NLMSG_DONE</c> carrying a status of zero completes the dump,
    ///     and one carrying a negative status fails it.
    /// </summary>
    [Fact]
    public void DoneWithAStatus_CompletesOnZeroAndFailsOnANegativeValue()
    {
        byte[] done = DoneMessage();
        Assert.Equal(
            NetlinkDumpStatus.Complete,
            NetlinkNeighborMessageParser.ParseDatagram(done, [])
        );

        byte[] failedDone = DoneMessage();
        BinaryPrimitives.WriteInt32LittleEndian(failedDone.AsSpan(16), -5);
        Assert.Equal(
            NetlinkDumpStatus.Failed,
            NetlinkNeighborMessageParser.ParseDatagram(failedDone, [])
        );
    }

    /// <summary>
    ///     This test checks that an <c>NLMSG_ERROR</c> with error 0 (an acknowledgement) is skipped
    ///     and the dump still completes at the following <c>NLMSG_DONE</c>.
    /// </summary>
    [Fact]
    public void AnAcknowledgementThenDone_CompletesTheDump()
    {
        byte[] datagram =
        [
            .. NeighborMessage(IPAddress.Parse("192.0.2.24"), Mac, NudStale),
            .. ErrorMessage(0),
            .. DoneMessage(),
        ];
        List<NeighborTableEntry> entries = [];

        NetlinkDumpStatus status = NetlinkNeighborMessageParser.ParseDatagram(datagram, entries);

        Assert.Equal(NetlinkDumpStatus.Complete, status);
        Assert.Single(entries);
    }

    /// <summary>
    ///     This test reads the test machine's neighbor table over an rtnetlink socket, which shows
    ///     the request encoding and native calls work against a kernel and not only against bytes
    ///     the tests built. On a non-Linux host it checks that the reader returns null.
    /// </summary>
    [Fact]
    public void ReadsTheRealKernelNeighborTableOnLinux()
    {
        IReadOnlyList<NeighborTableEntry> entries = new NetlinkNeighborTableReader().Read();

        if (!OperatingSystem.IsLinux())
        {
            Assert.Null(entries);
            return;
        }

        Assert.NotNull(entries);
        Assert.All(entries, entry => Assert.NotNull(entry.Address));
    }

    /// <summary>
    ///     This test checks that sampling records only confirmed-reachable entries that carry an
    ///     Ethernet MAC; a STALE or DELAY entry, or one with no MAC, is never a sighting.
    /// </summary>
    [Fact]
    public void SamplingRecordsOnlyConfirmedReachableEntries()
    {
        FakeNeighborTableReader reader = new([
            new NeighborTableEntry(
                IPAddress.Parse("192.0.2.1"),
                Mac,
                NetlinkNeighborMessageParser.NudReachable
            ),
            new NeighborTableEntry(IPAddress.Parse("192.0.2.2"), Mac, NudStale),
            new NeighborTableEntry(IPAddress.Parse("192.0.2.3"), Mac, NudDelay),
            new NeighborTableEntry(
                IPAddress.Parse("192.0.2.4"),
                null,
                NetlinkNeighborMessageParser.NudReachable
            ),
        ]);
        RecordingPresenceRecorder recorder = new();
        NeighborTablePresenceService service = new(
            reader,
            recorder,
            new StaticOptionsMonitor<ClientPresenceOptions>(new ClientPresenceOptions()),
            NullLogger<NeighborTablePresenceService>.Instance
        );

        Assert.Equal(1, service.SampleOnce());
        Assert.Equal(
            IPAddress.Parse("192.0.2.1"),
            Assert.Single(recorder.HardwareSightings).Address
        );
    }

    /// <summary>
    ///     This test checks that an unreadable neighbor table records nothing and does not throw.
    /// </summary>
    [Fact]
    public void AnUnsupportedPlatformRecordsNothing()
    {
        NeighborTablePresenceService service = new(
            new FakeNeighborTableReader(null),
            new RecordingPresenceRecorder(),
            new StaticOptionsMonitor<ClientPresenceOptions>(new ClientPresenceOptions()),
            NullLogger<NeighborTablePresenceService>.Instance
        );

        Assert.Equal(0, service.SampleOnce());
    }

    /// <summary>
    ///     This class returns a fixed neighbor-table snapshot.
    /// </summary>
    private sealed class FakeNeighborTableReader(IReadOnlyList<NeighborTableEntry> entries)
        : INeighborTableReader
    {
        /// <inheritdoc />
        public IReadOnlyList<NeighborTableEntry> Read() => entries;
    }
}

/// <summary>
///     This class records every hardware sighting it is handed, for assertions.
/// </summary>
internal sealed class RecordingPresenceRecorder : IClientPresenceRecorder
{
    /// <summary>Every hardware sighting recorded, in order.</summary>
    public List<(byte[] Mac, IPAddress Address)> HardwareSightings { get; } = [];

    /// <inheritdoc />
    public void RecordIdentity(
        string clientIdentifier,
        InternetProtocolVersionEnum protocolVersion,
        IPAddress address,
        DateTimeOffset seenAtUtc
    ) { }

    /// <inheritdoc />
    public void RecordHardwareAddress(
        byte[] macAddress,
        IPAddress address,
        DateTimeOffset seenAtUtc
    ) => HardwareSightings.Add((macAddress, address));

    /// <inheritdoc />
    public void RecordSourceAddress(IPAddress address, DateTimeOffset seenAtUtc) { }
}

/// <summary>
///     This class is a fixed-value <see cref="IOptionsMonitor{TOptions}" /> for tests.
/// </summary>
internal sealed class StaticOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
{
    /// <inheritdoc />
    public TOptions CurrentValue => value;

    /// <inheritdoc />
    public TOptions Get(string name) => value;

    /// <inheritdoc />
    public IDisposable OnChange(Action<TOptions, string> listener) => null;
}
