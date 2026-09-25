// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This record carries one parsed entry of a subscribed list for
///     <see cref="IDnsFilterListEntryRepository.ReplaceEntriesAsync" />. It holds only what the
///     list's source text can express; the repository fills in the other fields (identifier,
///     timestamps, owning list).
/// </summary>
/// <param name="Value">The normalized domain name the entry covers (itself and every subdomain).</param>
/// <param name="IsException">
///     Whether the entry is an exception that cancels matches in the same list rather than a
///     rule; see <see cref="Entity.DnsFilterListEntryEntity.IsException" />.
/// </param>
public sealed record DnsFilterListReplacementEntry(string Value, bool IsException = false);
