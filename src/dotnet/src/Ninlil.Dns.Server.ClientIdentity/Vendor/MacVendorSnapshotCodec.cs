using System.Globalization;
using System.IO.Compression;
using System.Text;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Vendor;

/// <summary>
///     This static class reads and writes Ninlil's compact vendor snapshot format. The same format
///     is used for the snapshot embedded in this assembly and for the refreshed copy persisted to
///     disk, so either can stand in for the other.
///     <para>
///         The format is Brotli-compressed UTF-8 text. Leading <c>#</c> lines carry metadata, tab
///         separated: <c>#ninlil-mac-vendors</c> and a format version, <c>#generated</c> and an
///         ISO 8601 instant, and one optional <c>#validator</c> line per registry (registry name,
///         <c>ETag</c>, <c>Last-Modified</c>). Every other line is one organization, a tab, then
///         every prefix assigned to it, comma separated, as uppercase hexadecimal; a prefix's
///         length (6, 7, or 9 digits) identifies its registry. Grouping prefixes under their
///         organization stores each name once, which is most of the saving over the IEEE files
///         (those also carry postal addresses, which this format drops).
///     </para>
///     <para>
///         <b>Regenerating the embedded snapshot</b>: run a server with this build, trigger a
///         refresh (<c>POST /api/mac-vendor-settings/refresh</c>, or let the automatic one run),
///         and copy the persisted file (<c>MacVendorLookupOptions.DataFilePath</c>, by default
///         <c>data/mac-vendors.snapshot.br</c> beside the application) over
///         <c>Vendor/Resources/mac-vendors.snapshot.br</c> in this project. The refresh path is the
///         only writer of this format, so the embedded copy is always produced by the same parser
///         and validation every running server uses.
///     </para>
/// </summary>
public static class MacVendorSnapshotCodec
{
    /// <summary>The magic first metadata key identifying this format.</summary>
    private const string FormatKey = "#ninlil-mac-vendors";

    /// <summary>The format version this codec writes and accepts.</summary>
    private const string FormatVersion = "1";

    /// <summary>The metadata key carrying the generation instant.</summary>
    private const string GeneratedKey = "#generated";

    /// <summary>The metadata key carrying one registry's cache validators.</summary>
    private const string ValidatorKey = "#validator";

    /// <summary>
    ///     This method writes a table in the snapshot format.
    /// </summary>
    /// <param name="stream">The stream to write the compressed snapshot to; left open.</param>
    /// <param name="table">The table to write.</param>
    public static void Write(Stream stream, MacVendorTable table)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(table);

        using BrotliStream brotli = new(stream, CompressionLevel.SmallestSize, leaveOpen: true);
        using StreamWriter writer = new(brotli, new UTF8Encoding(false)) { NewLine = "\n" };

        writer.WriteLine($"{FormatKey}\t{FormatVersion}");
        writer.WriteLine(
            $"{GeneratedKey}\t{table.GeneratedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)}"
        );

        foreach (MacVendorRegistryEnum registry in MacVendorRegistryExtension.All)
        {
            if (!table.Validators.TryGetValue(registry, out MacVendorRegistryValidator validator))
                continue;

            writer.WriteLine(
                $"{ValidatorKey}\t{registry.IeeeName}\t{Clean(validator.ETag)}\t{Clean(validator.LastModified)}"
            );
        }

        // Group every prefix under its organization, both sorted, so the output is deterministic
        // for identical input and compresses well.
        IEnumerable<IGrouping<string, MacVendorAssignment>> groups = MacVendorRegistryExtension
            .All.SelectMany(table.GetAssignments)
            .GroupBy(assignment => assignment.Organization, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (IGrouping<string, MacVendorAssignment> group in groups)
        {
            IEnumerable<string> prefixes = group
                .Select(assignment => assignment.FormatPrefix())
                .OrderBy(prefix => prefix.Length)
                .ThenBy(prefix => prefix, StringComparer.Ordinal);

            writer.WriteLine($"{group.Key}\t{string.Join(',', prefixes)}");
        }
    }

    /// <summary>
    ///     This method reads a table written by <see cref="Write" />.
    /// </summary>
    /// <param name="stream">The stream holding a compressed snapshot; left open.</param>
    /// <param name="source">Where the snapshot came from, recorded on the resulting table.</param>
    /// <returns>The table the snapshot describes.</returns>
    /// <exception cref="InvalidDataException">Thrown when the stream is not a well-formed snapshot.</exception>
    public static MacVendorTable Read(Stream stream, MacVendorDataSourceEnum source)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // The Brotli decoder reports corrupt input as an InvalidOperationException; surface it
        // as the same InvalidDataException every other malformation raises, so a caller needs
        // to handle exactly one kind of "not a usable snapshot".
        try
        {
            return ReadCore(stream, source);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException("The snapshot is not valid Brotli data.", exception);
        }
    }

    /// <summary>
    ///     This method does <see cref="Read" />'s actual decoding.
    /// </summary>
    private static MacVendorTable ReadCore(Stream stream, MacVendorDataSourceEnum source)
    {
        using BrotliStream brotli = new(stream, CompressionMode.Decompress, leaveOpen: true);
        using StreamReader reader = new(brotli, Encoding.UTF8);

        if (reader.ReadLine() != $"{FormatKey}\t{FormatVersion}")
            throw new InvalidDataException("Not a version 1 Ninlil MAC vendor snapshot.");

        DateTimeOffset? generatedAtUtc = null;
        Dictionary<MacVendorRegistryEnum, MacVendorRegistryValidator> validators = [];
        List<MacVendorAssignment> assignments = [];

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
                continue;

            string[] parts = line.Split('\t');

            // Metadata lines.
            if (parts[0] == GeneratedKey && parts.Length == 2)
            {
                generatedAtUtc = DateTimeOffset.Parse(
                    parts[1],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind
                );
                continue;
            }

            if (parts[0] == ValidatorKey && parts.Length == 4)
            {
                MacVendorRegistryEnum registry = MacVendorRegistryExtension.All.FirstOrDefault(
                    candidate => candidate.IeeeName == parts[1],
                    (MacVendorRegistryEnum)(-1)
                );
                if (MacVendorRegistryExtension.All.Contains(registry))
                    validators[registry] = new MacVendorRegistryValidator(
                        NullIfEmpty(parts[2]),
                        NullIfEmpty(parts[3])
                    );
                continue;
            }

            // An organization line: the name, then its comma-separated prefixes. Only the
            // metadata keys above, with their field counts, are metadata, so an organization whose
            // name begins with '#' still round-trips as an ordinary line.
            if (parts.Length != 2 || parts[0].Length == 0)
                throw new InvalidDataException($"Malformed snapshot line: '{line}'.");

            foreach (string prefixText in parts[1].Split(','))
            {
                if (
                    !MacVendorRegistryExtension.TryFromPrefixHexDigits(
                        prefixText.Length,
                        out MacVendorRegistryEnum registry
                    )
                    || !ulong.TryParse(
                        prefixText,
                        NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture,
                        out ulong prefix
                    )
                )
                    throw new InvalidDataException($"Malformed snapshot prefix: '{prefixText}'.");

                assignments.Add(new MacVendorAssignment(registry, prefix, parts[0]));
            }
        }

        if (generatedAtUtc is null)
            throw new InvalidDataException("The snapshot has no generation date.");

        return new MacVendorTable(assignments, generatedAtUtc.Value, source, validators);
    }

    /// <summary>
    ///     This method strips tabs and line breaks from a header value before it is written into
    ///     a tab-separated line, so a hostile or unusual header can never corrupt the file.
    /// </summary>
    private static string Clean(string value) =>
        value is null ? string.Empty : new string([.. value.Where(c => !char.IsControl(c))]);

    /// <summary>
    ///     This method maps an empty validator field back to <see langword="null" />.
    /// </summary>
    private static string NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
