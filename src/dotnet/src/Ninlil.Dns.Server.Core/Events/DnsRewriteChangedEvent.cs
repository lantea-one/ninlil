// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published whenever a DNS rewrite changes -- created, updated, deleted, or
///     enabled/disabled through <c>DnsRewritesController</c>. No local engine subscribes to this
///     for cache invalidation (<c>DnsRewriteEngine</c> re-reads its own repository fresh on every
///     query) -- today, its only real subscriber is the cluster's own config sync.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the rewrite changed.</param>
/// <param name="RewriteId">The identifier of the rewrite that changed.</param>
/// <param name="IsDeleted">
///     Whether this change was a deletion -- <see langword="true" /> only for
///     <c>DnsRewritesController.Delete</c>.
/// </param>
public record DnsRewriteChangedEvent(
    DateTimeOffset OccurredAtUtc,
    Guid RewriteId,
    bool IsDeleted = false
) : IDnsServerEvent;
