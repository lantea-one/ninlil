using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Filtering;

/// <summary>
///     This record is the JSON payload of a <c>DnsFilterListEntryEntity</c> config-sync envelope.
/// </summary>
/// <param name="ListId">The owning list's identifier; the list must exist on the receiver (see <c>FilterListEntryClusterConfigSyncApplier.CanApplyAsync</c>).</param>
/// <param name="Value">The entry's matched value.</param>
/// <param name="Kind">How <paramref name="Value" /> is matched.</param>
/// <param name="Comment">Free-form operator notes.</param>
public sealed record FilterListEntrySyncPayload(
    Guid ListId,
    string Value,
    DnsFilterEntryKindEnum Kind,
    string Comment
);
