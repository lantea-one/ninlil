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
///     This class seeds <see cref="IRecursiveCache" /> with the root zone's own name servers at
///     startup, so <see cref="IterativeResolutionEngine" /> never has to special-case "nothing
///     is cached yet" -- it always has at least the root to start walking from. The hardcoded
///     <see cref="RootHints" /> list is stored first, synchronously, so this resolver can
///     bootstrap even with no network access at all; an RFC 8109 priming query (an ordinary
///     <c>NS .</c> query, sent to one of those same root servers) is then attempted to refresh
///     it with whatever the root's own, currently-live answer actually is, which is preferred
///     over the hardcoded list once it succeeds.
/// </summary>
/// <param name="cache">The cache this service seeds.</param>
/// <param name="upstreamClient">The client used to send the priming query itself.</param>
/// <param name="options">Whether DNSSEC validation (and so, whether to request signed root data) is enabled.</param>
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
            // The hardcoded seed above already leaves this resolver able to bootstrap; a failed
            // priming query (no network access yet at startup, most commonly) is not fatal.
            logger.LogWarning(
                exception,
                "Failed to prime the root hints from a live root server; continuing with the hardcoded list."
            );
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    ///     This method stores the hardcoded <see cref="RootHints" /> list directly, with no
    ///     network access needed at all.
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
    ///     This method sends a real RFC 8109 priming query and, if it succeeds, replaces the
    ///     hardcoded seed above with the root's own, currently-live answer.
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
