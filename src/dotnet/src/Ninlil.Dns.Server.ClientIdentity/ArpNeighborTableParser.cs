using System.Globalization;
using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity;

/// <summary>
///     This class parses Linux's own real, kernel-exposed ARP neighbor table format
///     (<c>/proc/net/arp</c>) -- a plain, whitespace-columned text file, one fixed header line
///     followed by one line per known neighbor, each holding six whitespace-separated columns:
///     <c>IP address / HW type / Flags / HW address / Mask / Device</c>. This is a standard Linux
///     kernel interface (documented by, and re-derived directly from, the kernel's own
///     <c>net/ipv4/arp.c</c> output format and RFC 826's own address-resolution semantics), never
///     from any third-party tool's own reading of this same file -- see this project's own
///     clean-room licensing rule.
///     <para>
///         A real, worked example line, taken from this exact format:
///         <c>192.168.1.5      0x1         0x2         aa:bb:cc:dd:ee:ff     *        eth0</c>
///     </para>
/// </summary>
public static class ArpNeighborTableParser
{
    /// <summary>
    ///     This constant contains the placeholder hardware address the kernel writes for an
    ///     entry it has not yet resolved -- present in the file, but never a real mapping, and so
    ///     always excluded from this parser's own result.
    /// </summary>
    private const string UnresolvedHardwareAddress = "00:00:00:00:00:00";

    /// <summary>
    ///     This constant contains the one <c>Flags</c> bit (<c>ATF_COM</c>, "complete") this
    ///     parser requires an entry to carry before trusting its hardware address -- an entry
    ///     the kernel is still waiting on a reply for carries this bit cleared, per the same
    ///     kernel source this format is re-derived from.
    /// </summary>
    private const int CompleteFlagBit = 0x2;

    /// <summary>
    ///     This method parses the full text content of a real (or synthetically constructed, for
    ///     test purposes, in this exact real format) <c>/proc/net/arp</c> file into a list of
    ///     genuinely resolved entries.
    /// </summary>
    /// <param name="contents">The full text content of the ARP table file.</param>
    /// <returns>
    ///     Every row that carries a real, resolved hardware address -- an incomplete row (no
    ///     reply yet), and any row this parser cannot make sense of at all, are both silently
    ///     skipped rather than thrown for, since a single malformed or transiently-incomplete row
    ///     must never take this whole read down.
    /// </returns>
    public static IReadOnlyList<ArpNeighborTableEntry> Parse(string contents)
    {
        List<ArpNeighborTableEntry> entries = [];

        if (string.IsNullOrEmpty(contents))
            return entries;

        using StringReader reader = new(contents);

        // The first line is always the fixed column-header row ("IP address  HW type  Flags  HW
        // address  Mask  Device") -- discarded unconditionally, regardless of its exact text,
        // since this parser only ever reads the data rows that follow it.
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
    ///     This method parses a single data row, returning <see langword="null" /> for anything
    ///     that is not a genuinely resolved entry -- a blank line, a row with too few columns, an
    ///     unparseable IP address or flags value, or a row still carrying the kernel's own
    ///     unresolved placeholder hardware address.
    /// </summary>
    private static ArpNeighborTableEntry ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        // Every column is separated by one or more spaces/tabs -- a plain whitespace split, with
        // empty entries discarded, handles the kernel's own column alignment padding correctly.
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

        // A row still carrying the kernel's own placeholder means no reply has arrived yet --
        // never a real mapping, regardless of what the Flags column itself says.
        if (string.Equals(hardwareAddress, UnresolvedHardwareAddress, StringComparison.Ordinal))
            return null;

        if (!TryParseFlags(flagsText, out int flags))
            return null;

        if ((flags & CompleteFlagBit) == 0)
            return null;

        return new ArpNeighborTableEntry(ipAddress, hardwareAddress);
    }

    /// <summary>
    ///     This method parses the <c>Flags</c> column's own <c>"0x"</c>-prefixed hexadecimal
    ///     text into its integer value.
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
