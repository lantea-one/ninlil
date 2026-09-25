// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published when a DNS rewrite is created, updated, deleted, enabled or
///     disabled through <c>DnsRewritesController</c>. Cluster sync forwards it to peers; no
///     local cache needs it, because <c>DnsRewriteEngine</c> reads rewrites from storage.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the rewrite changed.</param>
/// <param name="RewriteId">The identifier of the rewrite that changed.</param>
/// <param name="IsDeleted">
///     Whether this change was a deletion (<see langword="true" /> only for
///     <c>DnsRewritesController.Delete</c>).
/// </param>
public record DnsRewriteChangedEvent(
    DateTimeOffset OccurredAtUtc,
    Guid RewriteId,
    bool IsDeleted = false
) : IDnsServerEvent;
