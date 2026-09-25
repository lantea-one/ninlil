using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.AccessControl;

namespace Ninlil.Dns.Server.Recursive.Tests.Fakes;

/// <summary>
///     This class is a fake IDnsAccessControlEngine that grants a capability to the addresses a
///     test configures, so RecursiveQueryResolver's recursion gate can be tested without the
///     access-control cache and repositories.
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
