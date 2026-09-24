// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Vendor;

/// <summary>
///     This class is one immutable, fully built MAC-prefix-to-organization table spanning all three
///     IEEE registries, answering a longest-prefix-match lookup for a 48-bit MAC address. It is
///     never mutated after construction: a refresh builds a brand-new table and swaps the
///     reference atomically, so a concurrent lookup always sees one complete, consistent table.
///     <para>
///         Each registry's prefixes are held in their own dictionary keyed by the prefix's numeric
///         value, with every distinct organization name stored once and referenced by index, so the
///         full registry set (tens of thousands of prefixes, but far fewer distinct organizations)
///         stays a few megabytes in memory. A lookup costs at most three dictionary probes.
///     </para>
/// </summary>
public sealed class MacVendorTable
{
    /// <summary>The distinct organization names every prefix entry indexes into.</summary>
    private readonly string[] _organizations;

    /// <summary>The prefix-to-organization-index map for each registry.</summary>
    private readonly Dictionary<MacVendorRegistryEnum, Dictionary<ulong, int>> _prefixes;

    /// <summary>
    ///     This constructor builds a table from a set of assignments. A prefix repeated within one
    ///     registry keeps its first occurrence.
    /// </summary>
    /// <param name="assignments">Every assignment, across any mix of registries.</param>
    /// <param name="generatedAtUtc">When the underlying data was downloaded from the IEEE.</param>
    /// <param name="source">Where this table's data was loaded from.</param>
    /// <param name="validators">The cache validators each registry's data was served with, where known.</param>
    public MacVendorTable(
        IEnumerable<MacVendorAssignment> assignments,
        DateTimeOffset generatedAtUtc,
        MacVendorDataSourceEnum source,
        IReadOnlyDictionary<MacVendorRegistryEnum, MacVendorRegistryValidator> validators = null
    )
    {
        ArgumentNullException.ThrowIfNull(assignments);

        GeneratedAtUtc = generatedAtUtc;
        Source = source;
        Validators =
            validators ?? new Dictionary<MacVendorRegistryEnum, MacVendorRegistryValidator>();

        _prefixes = MacVendorRegistryExtension.All.ToDictionary(
            registry => registry,
            _ => new Dictionary<ulong, int>()
        );

        // Intern every organization name once, so thousands of prefixes assigned to the same
        // organization share a single string.
        Dictionary<string, int> organizationIndexes = new(StringComparer.Ordinal);
        List<string> organizations = [];

        foreach (MacVendorAssignment assignment in assignments)
        {
            if (!organizationIndexes.TryGetValue(assignment.Organization, out int index))
            {
                index = organizations.Count;
                organizations.Add(assignment.Organization);
                organizationIndexes.Add(assignment.Organization, index);
            }

            _prefixes[assignment.Registry].TryAdd(assignment.Prefix, index);
        }

        _organizations = [.. organizations];
    }

    /// <summary>
    ///     This property contains when the underlying data was downloaded from the IEEE -- for the
    ///     embedded snapshot, when that snapshot was generated.
    /// </summary>
    public DateTimeOffset GeneratedAtUtc { get; }

    /// <summary>
    ///     This property contains where this table's data was loaded from.
    /// </summary>
    public MacVendorDataSourceEnum Source { get; }

    /// <summary>
    ///     This property contains the cache validators each registry's data was served with.
    /// </summary>
    public IReadOnlyDictionary<
        MacVendorRegistryEnum,
        MacVendorRegistryValidator
    > Validators { get; }

    /// <summary>
    ///     This property contains the total number of prefixes across every registry.
    /// </summary>
    public int TotalCount => _prefixes.Values.Sum(prefixes => prefixes.Count);

    /// <summary>
    ///     This method counts one registry's prefixes.
    /// </summary>
    /// <param name="registry">The registry to count.</param>
    /// <returns>How many prefixes that registry contributes.</returns>
    public int Count(MacVendorRegistryEnum registry) => _prefixes[registry].Count;

    /// <summary>
    ///     This method enumerates one registry's assignments -- used to write the table back out
    ///     and to carry an unchanged registry forward into a refreshed table.
    /// </summary>
    /// <param name="registry">The registry to enumerate.</param>
    /// <returns>Every assignment in that registry, in no particular order.</returns>
    public IEnumerable<MacVendorAssignment> GetAssignments(MacVendorRegistryEnum registry) =>
        _prefixes[registry]
            .Select(entry => new MacVendorAssignment(
                registry,
                entry.Key,
                _organizations[entry.Value]
            ));

    /// <summary>
    ///     This method finds the organization a MAC address's block is assigned to, preferring the
    ///     longest matching prefix: an MA-S assignment inside an MA-M block inside an MA-L block
    ///     held by the IEEE itself resolves to the MA-S holder, not to the IEEE.
    /// </summary>
    /// <param name="macAddress">A 6-octet MAC address.</param>
    /// <returns>The organization's name, or <see langword="null" /> when no registry lists the address's block.</returns>
    public string Lookup(ReadOnlySpan<byte> macAddress)
    {
        if (macAddress.Length != 6)
            return null;

        // Assemble the 48-bit address into one number, most significant octet first.
        ulong value = 0;
        foreach (byte octet in macAddress)
            value = (value << 8) | octet;

        // Probe from the longest prefix down to the shortest.
        for (int index = MacVendorRegistryExtension.All.Count - 1; index >= 0; index--)
        {
            MacVendorRegistryEnum registry = MacVendorRegistryExtension.All[index];
            ulong prefix = value >> (48 - registry.PrefixBits);
            if (_prefixes[registry].TryGetValue(prefix, out int organizationIndex))
                return _organizations[organizationIndex];
        }

        return null;
    }
}
