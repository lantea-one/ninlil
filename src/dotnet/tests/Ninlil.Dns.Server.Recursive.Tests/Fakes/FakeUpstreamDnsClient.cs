using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Recursive.Transport;

namespace Ninlil.Dns.Server.Recursive.Tests.Fakes;

/// <summary>
///     This class stands in for a real upstream name server: canned responses keyed by which
///     address was queried and what was asked, so <see cref="Resolution.IterativeResolutionEngine" />
///     can be exercised without any real network access.
/// </summary>
public sealed class FakeUpstreamDnsClient : IUpstreamDnsClient
{
    private readonly Dictionary<
        (IPAddress Address, DnsName Name, DnsResourceRecordTypeEnum Type),
        DnsMessage
    > _responses = [];

    public int QueryCount { get; private set; }

    /// <summary>
    ///     Every query this client has actually been asked, in order -- the exact name/type/ECS
    ///     shape a test needs to confirm RFC 9156 QNAME minimization genuinely sent a minimized
    ///     probe name (rather than a full query) at an intermediate hop, or that a probe carried
    ///     no client subnet option at all.
    /// </summary>
    public List<(
        IPAddress Address,
        DnsName Name,
        DnsResourceRecordTypeEnum Type,
        DnsEdnsOption ClientSubnetOption
    )> Queries { get; } = [];

    public void AddResponse(
        IPAddress address,
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsMessage response
    ) => _responses[(address, name, type)] = response;

    public Task<DnsMessage> QueryAsync(
        IPAddress serverAddress,
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        bool dnssecOk,
        DnsEdnsOption clientSubnetOption = null,
        CancellationToken stoppingToken = default
    )
    {
        QueryCount++;
        Queries.Add((serverAddress, name, type, clientSubnetOption));

        return Task.FromResult(_responses.GetValueOrDefault((serverAddress, name, type)));
    }
}
