using System.Globalization;
using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity;

/// <summary>
///     This class parses the Linux kernel's ARP table (<c>/proc/net/arp</c>) for
///     <see cref="ArpNeighborTableClientIdentityResolver" />. The file is plain text: one header
///     line, then one line per neighbor with six whitespace-separated columns:
///     <c>IP address / HW type / Flags / HW address / Mask / Device</c>. The format is taken from
///     the kernel's <c>net/ipv4/arp.c</c> and RFC 826, not from any other tool that reads the
///     file (see the clean-room policy in <c>NOTICE.md</c>).
///     <para>
///         An example line:
///         <c>192.168.1.5      0x1         0x2         aa:bb:cc:dd:ee:ff     *        eth0</c>
///     </para>
/// </summary>
public static class ArpNeighborTableParser
{
    /// <summary>
    ///     This constant contains the placeholder hardware address the kernel writes for an
    ///     unresolved entry. Such rows are never returned.
    /// </summary>
    private const string UnresolvedHardwareAddress = "00:00:00:00:00:00";

    /// <summary>
    ///     This constant contains the <c>Flags</c> bit (<c>ATF_COM</c>, "complete") an entry must
    ///     carry before its hardware address is trusted. The kernel clears it while it waits for a
    ///     reply.
    /// </summary>
    private const int CompleteFlagBit = 0x2;

    /// <summary>
    ///     This method parses the text of a <c>/proc/net/arp</c> file into its complete entries.
    /// </summary>
    /// <param name="contents">The full text content of the ARP table file.</param>
    /// <returns>
    ///     Every row with a resolved hardware address. Incomplete and unparseable rows are skipped
    ///     rather than thrown for, so one bad row cannot spoil the whole read.
    /// </returns>
    public static IReadOnlyList<ArpNeighborTableEntry> Parse(string contents)
    {
        List<ArpNeighborTableEntry> entries = [];

        if (string.IsNullOrEmpty(contents))
            return entries;

        using StringReader reader = new(contents);

        // The first line is the column header ("IP address  HW type  Flags  HW address  Mask
        // Device"); it is skipped whatever its text.
        _ = reader.ReadLine();

        string line;
        while ((line = reader.ReadLine()) != null)
        {
            ArpNeighborTableEntry entry = ParseLine(line);

            if (entry != null)
                entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    ///     This method parses one data row. It returns <see langword="null" /> for a blank line, a
    ///     row with too few columns, an unparseable IP address or flags value, the unresolved
    ///     placeholder hardware address, or a row without the complete flag.
    /// </summary>
    private static ArpNeighborTableEntry ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        // Columns are separated by runs of spaces or tabs (the kernel pads them to align), so
        // split on whitespace and drop empty entries.
        string[] columns = line.Split(
            (char[])null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

        if (columns.Length < 4)
            return null;

        string ipAddressText = columns[0];
        string flagsText = columns[2];
        string hardwareAddress = columns[3];

        if (!IPAddress.TryParse(ipAddressText, out IPAddress ipAddress))
            return null;

        // The placeholder means no reply has arrived yet, whatever the Flags column says.
        if (string.Equals(hardwareAddress, UnresolvedHardwareAddress, StringComparison.Ordinal))
            return null;

        if (!TryParseFlags(flagsText, out int flags))
            return null;

        if ((flags & CompleteFlagBit) == 0)
            return null;

        return new ArpNeighborTableEntry(ipAddress, hardwareAddress);
    }

    /// <summary>
    ///     This method parses the <c>"0x"</c>-prefixed hexadecimal <c>Flags</c> column.
    /// </summary>
    private static bool TryParseFlags(string flagsText, out int flags)
    {
        flags = 0;

        if (string.IsNullOrEmpty(flagsText))
            return false;

        string hexDigits = flagsText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? flagsText[2..]
            : flagsText;

        return int.TryParse(
            hexDigits,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out flags
        );
    }
}
