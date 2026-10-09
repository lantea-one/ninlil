using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Recursive.Transport;

namespace Ninlil.Dns.Server.Recursive.Tests.Fakes;

/// <summary>
///     This class is a fake upstream name server: canned responses keyed by the queried address,
///     name and type, so IterativeResolutionEngine runs without network access.
/// </summary>
public sealed class FakeUpstreamDnsClient : IUpstreamDnsClient
{
    private readonly Dictionary<
        (IPAddress Address, DnsName Name, DnsResourceRecordTypeEnum Type),
        DnsMessage
    > _responses = [];

    public int QueryCount { get; private set; }

    /// <summary>
    ///     This property holds an optional function that answers a query no canned response was
    ///     added for, so a test can serve a whole zone hierarchy
    ///     (<see cref="Support.SignedHierarchy" />) and still pin single answers with
    ///     <see cref="AddResponse" />. Canned responses win; the function runs only without one.
    /// </summary>
    public Func<IPAddress, DnsName, DnsResourceRecordTypeEnum, DnsMessage> Responder { get; set; }

    /// <summary>
    ///     This property contains every query received, in order, with its name, type and ECS
    ///     option, so tests can check QNAME minimization and ECS handling.
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

        if (_responses.TryGetValue((serverAddress, name, type), out DnsMessage canned))
            return Task.FromResult(canned);

        return Task.FromResult(Responder?.Invoke(serverAddress, name, type));
    }
}
