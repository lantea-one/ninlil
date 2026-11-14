// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This struct names a suffix of a <see cref="DnsName" /> by the name and the number of
///     leading labels to skip, without building the suffix. It is the alternate key of
///     <see cref="DnsNameComparer" />: a dictionary keyed by <see cref="DnsName" /> is probed with
///     it through <c>GetAlternateLookup</c>, so walking a query name up to the root allocates
///     nothing.
/// </summary>
/// <param name="Name">The name whose suffix is meant.</param>
/// <param name="SkipLabels">
///     How many labels to drop from the left, from 0 (the whole name) to the label count (the
///     root), as for <see cref="DnsName.Suffix" />.
/// </param>
public readonly record struct DnsNameSuffix(DnsName Name, int SkipLabels);
