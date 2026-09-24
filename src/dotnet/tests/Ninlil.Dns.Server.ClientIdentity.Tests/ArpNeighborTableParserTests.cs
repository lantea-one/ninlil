using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Tests;

/// <summary>
///     This class covers <see cref="ArpNeighborTableParser" /> against synthetic content written
///     in the exact real format the Linux kernel exposes at <c>/proc/net/arp</c> (verified
///     live against this machine's own real file before writing these fixtures) -- header
///     line, then whitespace-columned data rows of <c>IP address / HW type / Flags / HW
///     address / Mask / Device</c>.
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
        // waiting on an ARP reply for -- never a real mapping.
        string contents =
            $"{Header}\n192.168.1.9      0x1         0x0         00:00:00:00:00:00     *        eth0\n";

        IReadOnlyList<ArpNeighborTableEntry> entries = ArpNeighborTableParser.Parse(contents);

        Assert.Empty(entries);
    }

    [Fact]
    public void Parse_WithFlagsMissingTheCompleteBit_SkipsTheEntry()
    {
        // A real hardware address can, in principle, still be present with an unexpected Flags
        // value that does not carry ATF_COM (0x2) set -- this parser trusts only a genuinely
        // "complete" entry.
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
