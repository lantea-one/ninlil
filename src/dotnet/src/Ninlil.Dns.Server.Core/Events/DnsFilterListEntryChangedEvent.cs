// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published whenever a single entry within a block/allow list changes --
///     created, updated, or deleted through <c>FilterListEntriesController</c>. Only ever fires
///     for a <c>Local</c>-sourced list's own manually written entries -- a
///     <c>Subscribed</c> list's entries are wholesale-replaced by
///     <c>DnsFilterListExternalRefreshService</c> on its own independent schedule per node, and
///     that wholesale replace does not publish this event at all (each node refetches the same
///     external source independently, so there is nothing this event could usefully carry for
///     that case).
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the entry changed.</param>
/// <param name="ListId">The identifier of the list the entry belongs to.</param>
/// <param name="EntryId">The identifier of the entry that changed.</param>
/// <param name="IsDeleted">
///     Whether this change was a deletion -- <see langword="true" /> only for
///     <c>FilterListEntriesController.Delete</c>.
/// </param>
public record DnsFilterListEntryChangedEvent(
    DateTimeOffset OccurredAtUtc,
    Guid ListId,
    Guid EntryId,
    bool IsDeleted = false
) : IDnsServerEvent;
