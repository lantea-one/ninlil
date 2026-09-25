using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.AccessControl;

// Define our namespace.
namespace Ninlil.Dns.Server.RateLimiting.Tests.Fakes;

/// <summary>
///     This class is a fake <see cref="IDnsAccessControlEngine" /> that grants one capability to
///     the addresses a test lists with <see cref="Grant" />. It lets the tests exercise the
///     bypass check in <see cref="TokenBucketDnsRateLimiter" /> without the rule cache,
///     repository and event bus, which <c>Ninlil.Dns.Server.AccessControl.Tests</c> covers.
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
