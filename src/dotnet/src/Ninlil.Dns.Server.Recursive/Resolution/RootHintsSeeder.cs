using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Recursive.Caching;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Resolution;

/// <summary>
///     This class writes the root name server set (the NS RRset at the root plus each server's A
///     and AAAA records) into <see cref="IRecursiveCache" />. <see cref="RootHintsPrimingService" />
///     uses it at startup and on every re-prime, and <see cref="IterativeResolutionEngine" /> uses
///     it when the cache has no root name servers left, so the next query finds them cached.
/// </summary>
public static class RootHintsSeeder
{
    /// <summary>
    ///     This constant contains the TTL, in seconds, of the root hint entries (six days, the TTL
    ///     of the root zone's own NS set).
    /// </summary>
    public const uint HintTimeToLive = 518400;

    /// <summary>
    ///     This method stores a root hints list in the cache: the NS RRset for the root, and the
    ///     A and AAAA RRsets of each server.
    /// </summary>
    /// <param name="cache">The cache to write to.</param>
    /// <param name="rootServers">The effective hints list (see <see cref="RootHints.Resolve" />).</param>
    public static void Seed(IRecursiveCache cache, IReadOnlyList<RootHintServer> rootServers)
    {
        List<DnsResourceRecord> nsRecords =
        [
            .. rootServers.Select(server => new DnsResourceRecord(
                DnsName.Root,
                DnsResourceRecordTypeEnum.NS,
                DnsClassEnum.Internet,
                HintTimeToLive,
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

            StoreAddresses(
                cache,
                serverName,
                DnsResourceRecordTypeEnum.A,
                [
                    .. server
                        .Addresses.Where(address =>
                            address.AddressFamily == AddressFamily.InterNetwork
                        )
                        .Select(address => new DnsResourceRecord(
                            serverName,
                            DnsResourceRecordTypeEnum.A,
                            DnsClassEnum.Internet,
                            HintTimeToLive,
                            DnsRecordDataCodecRegistry.Encode(
                                DnsResourceRecordTypeEnum.A,
                                new ARecordData(address)
                            )
                        )),
                ]
            );
            StoreAddresses(
                cache,
                serverName,
                DnsResourceRecordTypeEnum.Aaaa,
                [
                    .. server
                        .Addresses.Where(address =>
                            address.AddressFamily == AddressFamily.InterNetworkV6
                        )
                        .Select(address => new DnsResourceRecord(
                            serverName,
                            DnsResourceRecordTypeEnum.Aaaa,
                            DnsClassEnum.Internet,
                            HintTimeToLive,
                            DnsRecordDataCodecRegistry.Encode(
                                DnsResourceRecordTypeEnum.Aaaa,
                                new AaaaRecordData(address)
                            )
                        )),
                ]
            );
        }
    }

    /// <summary>
    ///     This method stores the root NS RRset from a priming response, and every A and AAAA
    ///     record in its additional section, replacing the seeded entries.
    /// </summary>
    /// <param name="cache">The cache to write to.</param>
    /// <param name="nsRecords">The NS records for the root from the response's answer section.</param>
    /// <param name="additionalRecords">The response's additional section (the glue).</param>
    public static void StoreFromPrimingResponse(
        IRecursiveCache cache,
        IReadOnlyList<DnsResourceRecord> nsRecords,
        IReadOnlyList<DnsResourceRecord> additionalRecords
    )
    {
        cache.StorePositive(
            DnsName.Root,
            DnsResourceRecordTypeEnum.NS,
            DnsClassEnum.Internet,
            nsRecords,
            []
        );

        foreach (var group in additionalRecords.GroupBy(record => record.Name))
        {
            StoreAddresses(
                cache,
                group.Key,
                DnsResourceRecordTypeEnum.A,
                [.. group.Where(record => record.Type == DnsResourceRecordTypeEnum.A)]
            );
            StoreAddresses(
                cache,
                group.Key,
                DnsResourceRecordTypeEnum.Aaaa,
                [.. group.Where(record => record.Type == DnsResourceRecordTypeEnum.Aaaa)]
            );
        }
    }

    private static void StoreAddresses(
        IRecursiveCache cache,
        DnsName name,
        DnsResourceRecordTypeEnum type,
        List<DnsResourceRecord> records
    )
    {
        if (records.Count > 0)
            cache.StorePositive(name, type, DnsClassEnum.Internet, records, []);
    }
}
