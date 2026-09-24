// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This record carries one freshly parsed entry for
///     <see cref="IDnsFilterListEntryRepository.ReplaceEntriesAsync" /> -- just the parts a
///     subscribed list's source text can express, since every other field of the stored entry
///     (identifier, timestamps, owning list) is filled in by the repository itself.
/// </summary>
/// <param name="Value">The normalized domain name the entry covers (itself and every subdomain).</param>
/// <param name="IsException">
///     Whether the entry is an exception that cancels the same list's own matches, rather than a
///     rule -- see <see cref="Entity.DnsFilterListEntryEntity.IsException" />.
/// </param>
public sealed record DnsFilterListReplacementEntry(string Value, bool IsException = false);
