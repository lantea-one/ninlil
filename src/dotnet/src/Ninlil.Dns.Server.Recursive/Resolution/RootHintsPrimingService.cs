using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Recursive.Caching;
using Ninlil.Dns.Server.Recursive.Transport;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Resolution;

/// <summary>
///     This class seeds <see cref="IRecursiveCache" /> with the root name servers at startup, so
///     <see cref="IterativeResolutionEngine" /> always has the root to start from. It first
///     stores the <see cref="RootHints" /> list (or its override) with a six-day TTL, which needs
///     no network. It then sends an RFC 8109 priming query (<c>NS .</c>) to the first address of
///     the first root server and, if that answers, replaces the list with the root's answer and
///     glue. A failed priming query is logged and the list stays. This runs once, in
///     <see cref="StartAsync" />, reading the options snapshot.
/// </summary>
/// <param name="cache">The cache this service seeds.</param>
/// <param name="upstreamClient">The client used to send the priming query.</param>
/// <param name="options">The root hints override and whether to set DO on the priming query.</param>
/// <param name="logger">The logger this service records a failed priming query to.</param>
public sealed class RootHintsPrimingService(
    IRecursiveCache cache,
    IUpstreamDnsClient upstreamClient,
    IOptions<RecursiveServerOptions> options,
    ILogger<RootHintsPrimingService> logger
) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        SeedHardcodedRootHints();

        try
        {
            await PrimeFromALiveRootServerAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The seeded list is enough to bootstrap, so a failed priming query (commonly no
            // network yet at startup) is not fatal.
            logger.LogWarning(
                exception,
                "Failed to prime the root hints from a live root server; continuing with the hardcoded list."
            );
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    ///     This method stores the root hints list (NS, A and AAAA records) in the cache.
    /// </summary>
    private void SeedHardcodedRootHints()
    {
        IReadOnlyList<RootHintServer> rootServers = RootHints.Resolve(options.Value);

        List<DnsResourceRecord> nsRecords =
        [
            .. rootServers.Select(server => new DnsResourceRecord(
                DnsName.Root,
                DnsResourceRecordTypeEnum.NS,
                DnsClassEnum.Internet,
                518400,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.NS,
                    new NsRecordData(DnsName.Parse(server.Name))
                )
            )),
        ];
        cache.StorePositive(
            DnsName.Root,
            DnsResourceRecordTypeEnum.NS,
            DnsClassEnum.Internet,
            nsRecords,
            []
        );

        foreach (RootHintServer server in rootServers)
        {
            DnsName serverName = DnsName.Parse(server.Name);

            List<DnsResourceRecord> aRecords =
            [
                .. server
                    .Addresses.Where(address =>
                        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                    )
                    .Select(address => new DnsResourceRecord(
                        serverName,
                        DnsResourceRecordTypeEnum.A,
                        DnsClassEnum.Internet,
                        518400,
                        DnsRecordDataCodecRegistry.Encode(
                            DnsResourceRecordTypeEnum.A,
                            new ARecordData(address)
                        )
                    )),
            ];
            List<DnsResourceRecord> aaaaRecords =
            [
                .. server
                    .Addresses.Where(address =>
                        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                    )
                    .Select(address => new DnsResourceRecord(
                        serverName,
                        DnsResourceRecordTypeEnum.Aaaa,
                        DnsClassEnum.Internet,
                        518400,
                        DnsRecordDataCodecRegistry.Encode(
                            DnsResourceRecordTypeEnum.Aaaa,
                            new AaaaRecordData(address)
                        )
                    )),
            ];

            if (aRecords.Count > 0)
                cache.StorePositive(
                    serverName,
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet,
                    aRecords,
                    []
                );

            if (aaaaRecords.Count > 0)
                cache.StorePositive(
                    serverName,
                    DnsResourceRecordTypeEnum.Aaaa,
                    DnsClassEnum.Internet,
                    aaaaRecords,
                    []
                );
        }
    }

    /// <summary>
    ///     This method sends the RFC 8109 priming query and, if it returns NS records, stores them
    ///     and every A/AAAA record in the additional section, replacing the seed. The response is
    ///     not DNSSEC-validated.
    /// </summary>
    private async Task PrimeFromALiveRootServerAsync(CancellationToken cancellationToken)
    {
        RootHintServer firstServer = RootHints.Resolve(options.Value)[0];
        System.Net.IPAddress serverAddress = firstServer.Addresses[0];

        DnsMessage response = await upstreamClient.QueryAsync(
            serverAddress,
            DnsName.Root,
            DnsResourceRecordTypeEnum.NS,
            DnsClassEnum.Internet,
            options.Value.EnableDnssecValidation,
            clientSubnetOption: null,
            stoppingToken: cancellationToken
        );

        List<DnsResourceRecord> nsRecords =
        [
            .. response?.Answers.Where(record => record.Type == DnsResourceRecordTypeEnum.NS) ?? [],
        ];

        if (nsRecords.Count == 0)
            return;

        cache.StorePositive(
            DnsName.Root,
            DnsResourceRecordTypeEnum.NS,
            DnsClassEnum.Internet,
            nsRecords,
            []
        );

        foreach (var group in response.AdditionalRecords.GroupBy(record => record.Name))
        {
            List<DnsResourceRecord> aRecords =
            [
                .. group.Where(record => record.Type == DnsResourceRecordTypeEnum.A),
            ];
            List<DnsResourceRecord> aaaaRecords =
            [
                .. group.Where(record => record.Type == DnsResourceRecordTypeEnum.Aaaa),
            ];

            if (aRecords.Count > 0)
                cache.StorePositive(
                    group.Key,
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet,
                    aRecords,
                    []
                );

            if (aaaaRecords.Count > 0)
                cache.StorePositive(
                    group.Key,
                    DnsResourceRecordTypeEnum.Aaaa,
                    DnsClassEnum.Internet,
                    aaaaRecords,
                    []
                );
        }
    }
}
