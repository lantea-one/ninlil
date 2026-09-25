// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published when one entry of a block/allow list is created, updated or
///     deleted through <c>FilterListEntriesController</c>, and cluster sync forwards it. It only
///     fires for hand-written entries of a <c>Local</c> list. A <c>Subscribed</c> list is
///     replaced wholesale by <c>DnsFilterListExternalRefreshService</c> on each node from the
///     same source, so that refresh publishes nothing.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the entry changed.</param>
/// <param name="ListId">The identifier of the list the entry belongs to.</param>
/// <param name="EntryId">The identifier of the entry that changed.</param>
/// <param name="IsDeleted">
///     Whether this change was a deletion (<see langword="true" /> only for
///     <c>FilterListEntriesController.Delete</c>).
/// </param>
public record DnsFilterListEntryChangedEvent(
    DateTimeOffset OccurredAtUtc,
    Guid ListId,
    Guid EntryId,
    bool IsDeleted = false
) : IDnsServerEvent;
