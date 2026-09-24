// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published whenever a block/allow list's own metadata changes -- created,
///     updated, deleted, or enabled/disabled through <c>FilterListsController</c>. Unlike
///     <see cref="DnsAccessControlRuleChangedEvent" />, no local engine subscribes to this purely
///     for cache invalidation (<c>DnsFilterEngine</c> re-reads its repositories fresh on every
///     query, per its own doc comment) -- today, its only real subscriber is the cluster's own
///     config sync, forwarding the change to every peer over the existing cluster channel.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the list changed.</param>
/// <param name="ListId">The identifier of the list that changed.</param>
/// <param name="IsDeleted">
///     Whether this change was a deletion -- <see langword="true" /> only for
///     <c>FilterListsController.Delete</c>; a create, replace, or enable/disable toggle all
///     report <see langword="false" />.
/// </param>
public record DnsFilterListChangedEvent(
    DateTimeOffset OccurredAtUtc,
    Guid ListId,
    bool IsDeleted = false
) : IDnsServerEvent;
