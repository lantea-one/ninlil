using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Filtering;

/// <summary>
///     This record is the JSON shape a <c>DnsFilterListEntryEntity</c>'s own config-sync
///     envelope carries.
/// </summary>
/// <param name="ListId">The owning list's identifier -- must already exist locally for this entry to be applied, see <c>FilterListEntryClusterConfigSyncApplier</c>'s own <c>CanApplyAsync</c> override.</param>
/// <param name="Value">The entry's own matched value.</param>
/// <param name="Kind">How <paramref name="Value" /> is matched.</param>
/// <param name="Comment">Free-form operator notes.</param>
public sealed record FilterListEntrySyncPayload(
    Guid ListId,
    string Value,
    DnsFilterEntryKindEnum Kind,
    string Comment
);
