using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.AccessControl;

// Define our namespace.
namespace Ninlil.Dns.Server.AccessControl;

/// <summary>
///     This class implements <see cref="IDnsAccessControlEngine" />, which answers "does this
///     client address hold this capability?" for the recursive resolver (recursion access) and
///     <c>TokenBucketDnsRateLimiter</c> (rate-limit bypass). It is a thin wrapper over the
///     in-memory check in <see cref="DnsAccessControlRuleCache" />, so a check never touches the
///     database and completes synchronously. It is registered as a singleton.
/// </summary>
/// <param name="cache">The cache, loaded at startup, that answers every check.</param>
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
