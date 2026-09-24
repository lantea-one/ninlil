// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Vendor;

/// <summary>
///     This record is the outcome of parsing one IEEE registry CSV file.
/// </summary>
/// <param name="Assignments">Every well-formed assignment, in file order, first occurrence of a prefix only.</param>
/// <param name="RowCount">How many data rows (excluding the header) the file held.</param>
/// <param name="MalformedRowCount">How many of those rows could not be interpreted and were skipped.</param>
public sealed record MacVendorRegistryParseResult(
    IReadOnlyList<MacVendorAssignment> Assignments,
    int RowCount,
    int MalformedRowCount
);
