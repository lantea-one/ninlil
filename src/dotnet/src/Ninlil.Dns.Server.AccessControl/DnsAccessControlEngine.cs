using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.AccessControl;

// Define our namespace.
namespace Ninlil.Dns.Server.AccessControl;

/// <summary>
///     This class implements <see cref="IDnsAccessControlEngine" /> as a thin, synchronous-under-
///     the-hood wrapper around <see cref="DnsAccessControlRuleCache" />'s own in-memory
///     CIDR-containment check -- the exact "define the abstraction in <c>Server.Core</c>,
///     implement it against an already-warm cache" shape
///     <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine</c> establishes for
///     <c>Ninlil.Dns.Server.Filtering.IDnsFilterEngine</c>, registered as a singleton the
///     same way.
/// </summary>
/// <param name="cache">The already-warm cache this engine answers every check from.</param>
public sealed class DnsAccessControlEngine(DnsAccessControlRuleCache cache)
    : IDnsAccessControlEngine
{
    /// <inheritdoc />
    public ValueTask<bool> HasCapabilityAsync(
        IPAddress remoteAddress,
        DnsAccessControlCapabilityEnum capability,
        CancellationToken stoppingToken = default
    ) => ValueTask.FromResult(cache.HasCapability(remoteAddress, capability));
}
