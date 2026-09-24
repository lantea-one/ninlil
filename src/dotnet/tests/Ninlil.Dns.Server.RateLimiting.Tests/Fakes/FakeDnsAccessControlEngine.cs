using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.AccessControl;

namespace Ninlil.Dns.Server.RateLimiting.Tests.Fakes;

/// <summary>
///     This class stands in for a real <see cref="IDnsAccessControlEngine" />, granting a
///     capability to exactly the addresses a test configures -- so
///     <see cref="TokenBucketDnsRateLimiter" />'s own real bypass-check widening can be exercised
///     deterministically, without needing the real cache/repository/event-bus machinery
///     <c>Ninlil.Dns.Server.AccessControl.Tests</c> already covers directly.
/// </summary>
public sealed class FakeDnsAccessControlEngine : IDnsAccessControlEngine
{
    private readonly HashSet<IPAddress> _grantedAddresses = [];
    private readonly DnsAccessControlCapabilityEnum _grantedCapability;

    public FakeDnsAccessControlEngine(DnsAccessControlCapabilityEnum grantedCapability) =>
        _grantedCapability = grantedCapability;

    public void Grant(IPAddress address) => _grantedAddresses.Add(address);

    public ValueTask<bool> HasCapabilityAsync(
        IPAddress remoteAddress,
        DnsAccessControlCapabilityEnum capability,
        CancellationToken stoppingToken = default
    ) =>
        ValueTask.FromResult(
            capability == _grantedCapability && _grantedAddresses.Contains(remoteAddress)
        );
}
