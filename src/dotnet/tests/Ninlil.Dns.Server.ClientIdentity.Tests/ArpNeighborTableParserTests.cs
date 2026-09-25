using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Tests;

/// <summary>
///     This class tests <see cref="ArpNeighborTableParser" /> against constructed text in the
///     format the Linux kernel uses for <c>/proc/net/arp</c>: a header line, then
///     whitespace-separated rows of <c>IP address / HW type / Flags / HW address / Mask /
///     Device</c>.
/// </summary>
public sealed class ArpNeighborTableParserTests
{
    private const string Header =
        "IP address       HW type     Flags       HW address            Mask     Device";

    [Fact]
    public void Parse_WithARealCompleteEntry_ReturnsIt()
    {
        string contents =
            $"{Header}\n192.168.1.5      0x1         0x2         aa:bb:cc:dd:ee:ff     *        eth0\n";

        IReadOnlyList<ArpNeighborTableEntry> entries = ArpNeighborTableParser.Parse(contents);

        Assert.Single(entries);
        Assert.Equal(IPAddress.Parse("192.168.1.5"), entries[0].IpAddress);
        Assert.Equal("aa:bb:cc:dd:ee:ff", entries[0].HardwareAddress);
    }

    [Fact]
    public void Parse_WithMultipleCompleteEntries_ReturnsAllOfThem()
    {
        string contents =
            $"{Header}\n"
            + "192.168.1.5      0x1         0x2         aa:bb:cc:dd:ee:ff     *        eth0\n"
            + "10.0.0.9         0x1         0x2         11:22:33:44:55:66     *        eth0\n";

        IReadOnlyList<ArpNeighborTableEntry> entries = ArpNeighborTableParser.Parse(contents);

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void Parse_WithAnIncompleteEntry_SkipsTheUnresolvedPlaceholderAddress()
    {
        // The kernel writes the all-zero placeholder, with Flags 0x0, for an address it is still
        // waiting for an ARP reply for.
        string contents =
            $"{Header}\n192.168.1.9      0x1         0x0         00:00:00:00:00:00     *        eth0\n";

        IReadOnlyList<ArpNeighborTableEntry> entries = ArpNeighborTableParser.Parse(contents);

        Assert.Empty(entries);
    }

    [Fact]
    public void Parse_WithFlagsMissingTheCompleteBit_SkipsTheEntry()
    {
        // A hardware address can appear with a Flags value that lacks ATF_COM (0x2); only a
        // complete entry is trusted.
        string contents =
            $"{Header}\n192.168.1.9      0x1         0x4         aa:bb:cc:dd:ee:ff     *        eth0\n";

        IReadOnlyList<ArpNeighborTableEntry> entries = ArpNeighborTableParser.Parse(contents);

        Assert.Empty(entries);
    }

    [Fact]
    public void Parse_WithOnlyTheHeaderLine_ReturnsNoEntries()
    {
        IReadOnlyList<ArpNeighborTableEntry> entries = ArpNeighborTableParser.Parse(Header + "\n");

        Assert.Empty(entries);
    }

    [Fact]
    public void Parse_WithEmptyContent_ReturnsNoEntries()
    {
        IReadOnlyList<ArpNeighborTableEntry> entries = ArpNeighborTableParser.Parse(string.Empty);

        Assert.Empty(entries);
    }

    [Fact]
    public void Parse_WithNullContent_ReturnsNoEntries()
    {
        IReadOnlyList<ArpNeighborTableEntry> entries = ArpNeighborTableParser.Parse(null);

        Assert.Empty(entries);
    }

    [Fact]
    public void Parse_WithATrailingBlankLine_SkipsItRatherThanThrowing()
    {
        string contents =
            $"{Header}\n192.168.1.5      0x1         0x2         aa:bb:cc:dd:ee:ff     *        eth0\n\n";

        IReadOnlyList<ArpNeighborTableEntry> entries = ArpNeighborTableParser.Parse(contents);

        Assert.Single(entries);
    }

    [Fact]
    public void Parse_WithAMalformedRowMissingColumns_SkipsItRatherThanThrowing()
    {
        string contents = $"{Header}\nthis-row-is-not-real-arp-data\n";

        IReadOnlyList<ArpNeighborTableEntry> entries = ArpNeighborTableParser.Parse(contents);

        Assert.Empty(entries);
    }

    [Fact]
    public void Parse_WithAnUnparseableIpAddress_SkipsItRatherThanThrowing()
    {
        string contents =
            $"{Header}\nnot-an-ip-address 0x1         0x2         aa:bb:cc:dd:ee:ff     *        eth0\n";

        IReadOnlyList<ArpNeighborTableEntry> entries = ArpNeighborTableParser.Parse(contents);

        Assert.Empty(entries);
    }

    [Fact]
    public void Parse_WithAnUnparseableFlagsColumn_SkipsItRatherThanThrowing()
    {
        string contents =
            $"{Header}\n192.168.1.5      0x1         not-hex     aa:bb:cc:dd:ee:ff     *        eth0\n";

        IReadOnlyList<ArpNeighborTableEntry> entries = ArpNeighborTableParser.Parse(contents);

        Assert.Empty(entries);
    }
}
