using System.Globalization;
using System.Text;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Vendor;

/// <summary>
///     This static class parses one of the registry CSV files the IEEE Registration Authority
///     publishes (<c>oui.csv</c>, <c>mam.csv</c>, <c>oui36.csv</c>). The format is taken from
///     those files: a header row of <c>Registry,Assignment,Organization Name,
///     Organization Address</c>, then one row per assignment, fields quoted per RFC 4180 whenever
///     they contain a comma or a double quote (a literal quote doubled), lines ending in CRLF.
///     Only the first three columns are read; the postal address is never kept.
///     <para>
///         Parsing is strict about shape and lenient about content: a row whose registry name,
///         prefix length, or prefix digits do not match the registry being parsed, or whose
///         organization name is blank, is counted as malformed and skipped rather than guessed
///         at -- the caller decides whether too many such rows make the whole file untrustworthy.
///         A prefix listed more than once keeps its first occurrence.
///     </para>
/// </summary>
public static class MacVendorRegistryCsvParser
{
    /// <summary>
    ///     This method parses a whole registry CSV file.
    /// </summary>
    /// <param name="reader">The reader positioned at the start of the file.</param>
    /// <param name="registry">The registry the file is expected to hold.</param>
    /// <returns>The parsed assignments and row statistics.</returns>
    /// <exception cref="FormatException">Thrown when the header row is missing or is not IEEE's registry header.</exception>
    public static MacVendorRegistryParseResult Parse(
        TextReader reader,
        MacVendorRegistryEnum registry
    )
    {
        ArgumentNullException.ThrowIfNull(reader);

        // The header is the one thing that must be exactly right: anything else (an HTML error
        // page served with a 200, a truncated body) is rejected outright rather than parsed.
        List<string> header = ReadRecord(reader);
        if (
            header is null
            || header.Count < 3
            || !string.Equals(
                header[0].Trim().TrimStart('\uFEFF'),
                "Registry",
                StringComparison.Ordinal
            )
            || !string.Equals(header[1].Trim(), "Assignment", StringComparison.Ordinal)
            || !string.Equals(header[2].Trim(), "Organization Name", StringComparison.Ordinal)
        )
            throw new FormatException(
                $"The {registry.IeeeName} registry file does not begin with the IEEE registry header row."
            );

        List<MacVendorAssignment> assignments = [];
        HashSet<ulong> seenPrefixes = [];
        int rowCount = 0;
        int malformedRowCount = 0;

        // Read every remaining record, skipping blank lines (a trailing newline, for instance).
        while (ReadRecord(reader) is { } record)
        {
            if (record.Count == 1 && string.IsNullOrWhiteSpace(record[0]))
                continue;

            rowCount++;

            if (!TryParseRow(record, registry, out MacVendorAssignment assignment))
            {
                malformedRowCount++;
                continue;
            }

            if (seenPrefixes.Add(assignment.Prefix))
                assignments.Add(assignment);
        }

        return new MacVendorRegistryParseResult(assignments, rowCount, malformedRowCount);
    }

    /// <summary>
    ///     This method interprets one data row, or reports it malformed.
    /// </summary>
    private static bool TryParseRow(
        List<string> record,
        MacVendorRegistryEnum registry,
        out MacVendorAssignment assignment
    )
    {
        assignment = default;

        if (record.Count < 3)
            return false;

        // The registry column must name the registry this file is supposed to hold.
        if (!string.Equals(record[0].Trim(), registry.IeeeName, StringComparison.Ordinal))
            return false;

        // The assignment is a fixed-width hexadecimal prefix.
        string prefixText = record[1].Trim();
        if (
            prefixText.Length != registry.PrefixHexDigits
            || !ulong.TryParse(
                prefixText,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out ulong prefix
            )
        )
            return false;

        string organization = NormalizeOrganization(record[2]);
        if (organization.Length == 0)
            return false;

        assignment = new MacVendorAssignment(registry, prefix, organization);
        return true;
    }

    /// <summary>
    ///     This method trims an organization name and replaces any control character (a tab or
    ///     line break, which the snapshot format uses as delimiters) with a plain space, collapsing
    ///     the resulting runs of whitespace.
    /// </summary>
    /// <param name="organization">The raw organization field.</param>
    /// <returns>The normalized name; empty when nothing printable remains.</returns>
    public static string NormalizeOrganization(string organization)
    {
        if (string.IsNullOrWhiteSpace(organization))
            return string.Empty;

        StringBuilder builder = new(organization.Length);
        bool previousWasSpace = false;

        foreach (char character in organization.Trim())
        {
            bool isSpace = char.IsWhiteSpace(character) || char.IsControl(character);
            if (isSpace && previousWasSpace)
                continue;

            builder.Append(isSpace ? ' ' : character);
            previousWasSpace = isSpace;
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    ///     This method reads one RFC 4180 record: comma-separated fields, each optionally wrapped
    ///     in double quotes (inside which a comma or line break is literal and a doubled quote is
    ///     one literal quote), ended by CRLF, LF, or CR outside quotes.
    /// </summary>
    /// <param name="reader">The reader to consume the record from.</param>
    /// <returns>The record's fields, or <see langword="null" /> at end of input.</returns>
    internal static List<string> ReadRecord(TextReader reader)
    {
        int next = reader.Read();
        if (next < 0)
            return null;

        List<string> fields = [];
        StringBuilder field = new();
        bool inQuotes = false;
        bool fieldStarted = false;

        while (next >= 0)
        {
            char character = (char)next;

            if (inQuotes)
            {
                if (character == '"')
                {
                    // A doubled quote is one literal quote; a lone one closes the quoted section.
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(character);
                }
            }
            else if (character == '"' && !fieldStarted)
            {
                // A quote opening a field starts a quoted section; anywhere else it is literal.
                inQuotes = true;
                fieldStarted = true;
            }
            else if (character == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
            }
            else if (character is '\r' or '\n')
            {
                // Swallow the LF of a CRLF pair, then end the record.
                if (character == '\r' && reader.Peek() == '\n')
                    reader.Read();
                break;
            }
            else
            {
                field.Append(character);
                fieldStarted = true;
            }

            next = reader.Read();
        }

        fields.Add(field.ToString());
        return fields;
    }
}
