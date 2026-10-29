using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EngineHosts;
using Ninlil.Dns.Server.Authoritative.Cache;

// Define our namespace.
namespace Ninlil.Benchmarks.Pipeline;

/// <summary>
///     This class generates and writes the data set the pipeline benchmarks run on: one SQLite
///     file per scale, <c>ninlil_bench_pipeline_&lt;scale&gt;_v1.sqlite3</c> in
///     <see cref="BenchmarkEnvironment.DataDirectory" />. It is separate from
///     <see cref="BenchmarkDataSet" /> because the pipeline classes run on SQLite only: after
///     warm-up every cache holds what a query needs, so the engine is never touched and a
///     second engine would only measure noise (the host proves it in its setup).
///     <para>
///         The set holds one primary zone (<see cref="ZoneName" />) with 100 records, two domain
///         block lists of <see cref="BenchmarkDataSetShape.EntriesPerList" /> entries in the
///         shapes of <see cref="BenchmarkDataSet.FilterEntryValue" /> (5 % wildcard or regex,
///         1 % exceptions), one response-IP list of 100 CIDRs, one client-source list of 50
///         CIDRs, 200 rewrites, one access rule that lets the client network use the forwarder,
///         a default group and one client group with two networks and one
///         client, and one global forwarder whose address <see cref="PipelineBenchmarkHost" />
///         points at the fake upstream. The last row written is a marker zone; a file that has
///         it is complete and is reused. Change <see cref="Version" /> whenever the generator
///         changes.
///     </para>
/// </summary>
public static class PipelineDataSet
{
    /// <summary>
    ///     This constant contains the version of the generator; it is part of the file name and
    ///     of the marker zone name.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    ///     This constant contains the apex of the hosted zone.
    /// </summary>
    public const string ZoneName = "bench.test.";

    /// <summary>
    ///     This constant contains the number of rewrites.
    /// </summary>
    public const int RewriteCount = 200;

    /// <summary>
    ///     This property contains the name of the marker zone that is written last.
    /// </summary>
    public static string MarkerZoneName => $"pipeline-seed-complete-v{Version}.bench.";

    /// <summary>
    ///     This method returns the path of the SQLite file of <paramref name="scale" />.
    /// </summary>
    public static string FilePath(BenchmarkScaleEnum scale) =>
        Path.Combine(
            BenchmarkEnvironment.DataDirectory,
            $"ninlil_bench_pipeline_{scale.ToString().ToLowerInvariant()}_v{Version}.sqlite3"
        );

    /// <summary>
    ///     This method returns the connection string of the SQLite file of
    ///     <paramref name="scale" />.
    /// </summary>
    public static string ConnectionString(BenchmarkScaleEnum scale) =>
        $"Data Source={FilePath(scale)}";

    /// <summary>
    ///     This method returns a name of the hosted zone that holds an A record
    ///     (<c>a0.bench.test.</c> to <c>a24.bench.test.</c>).
    /// </summary>
    public static string HostedName(int index) => $"a{index}.{ZoneName}";

    /// <summary>
    ///     This method returns a name that only the wildcard entry in the middle of the first
    ///     block list matches, so the matcher scans about half of the patterns before it hits.
    /// </summary>
    public static string PatternBlockedName(BenchmarkScaleEnum scale)
    {
        int entry = BenchmarkDataSet.ShapeFor(scale).EntriesPerList / 80 * 40;
        return $"www.wild{entry}.list0.blocked.test";
    }

    /// <summary>
    ///     This method returns the name of rewrite <paramref name="index" />.
    /// </summary>
    public static string RewriteName(int index) => $"rewrite{index}.home.test";

    /// <summary>
    ///     This method opens (and creates) the SQLite file of <paramref name="scale" /> and seeds
    ///     it unless it already holds a complete data set. It returns whether it wrote anything.
    /// </summary>
    /// <param name="scale">The size of the data set.</param>
    /// <param name="log">Where progress lines go.</param>
    public static async Task<bool> EnsureSeededAsync(BenchmarkScaleEnum scale, TextWriter log)
    {
        Directory.CreateDirectory(BenchmarkEnvironment.DataDirectory);

        await using EngineDatabase database = await EngineHost.BuildDatabaseAsync(
            StorageEngineEnum.Sqlite,
            ConnectionString(scale),
            createSchema: true
        );

        await using (AsyncServiceScope scope = database.Services.CreateAsyncScope())
        {
            IZoneRepository zones = scope.ServiceProvider.GetRequiredService<IZoneRepository>();

            if (await zones.FindByNameAsync(MarkerZoneName) != null)
                return false;
        }

        await log.WriteLineAsync($"[pipeline] seeding {FilePath(scale)}");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        BenchmarkDataSetShape shape = BenchmarkDataSet.ShapeFor(scale);
        SeedWriter writer = new(database.Services, log);

        ZoneEntity zone = NewZone(ZoneName, now);
        await writer.AddAllAsync<ZoneEntity, IZoneRepository>("zone", [zone]);
        await writer.AddAllAsync<RecordEntity, IRecordRepository>("records", Records(zone, now));
        await SeedFilteringAsync(writer, shape, now);
        await SeedRewritesAndForwarderAsync(writer, now);

        // Forwarding is recursive service, which only the clients of a rule with the recursion
        // capability get while recursion itself is off.
        await writer.AddAllAsync<DnsAccessControlRuleEntity, IDnsAccessControlRuleRepository>(
            "access rules",
            [
                new DnsAccessControlRuleEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    Name = "pipeline-recursion",
                    Cidr = "10.0.0.0/8",
                    Capabilities = DnsAccessControlCapabilityEnum.RecursionAccess,
                },
            ]
        );

        // The marker goes last: a seed that stopped half way leaves partial rows, so the file
        // has to be deleted before the next run.
        await writer.AddAllAsync<ZoneEntity, IZoneRepository>(
            "marker",
            [NewZone(MarkerZoneName, now)]
        );

        return true;
    }

    private static ZoneEntity NewZone(string name, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = now,
            Name = name,
            Type = ZoneTypeEnum.Primary,
        };

    private static RecordEntity Record(
        ZoneEntity zone,
        string name,
        DnsResourceRecordTypeEnum type,
        IDnsRecordData data,
        DateTimeOffset now
    )
    {
        // The codec writes the record the way the zone editor does, so the cache decodes it.
        DnsResourceRecord wire = new(
            DnsName.Parse(name),
            type,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.TryGetCodec(type, out IDnsRecordDataCodec codec)
                ? codec.Write(data)
                : throw new InvalidOperationException($"No codec for {type}.")
        );
        RecordEntity entity = RecordEntityCodec.FromWireRecord(wire, zone.Id);
        entity.CreatedAtUtc = now;
        return entity;
    }

    private static IEnumerable<RecordEntity> Records(ZoneEntity zone, DateTimeOffset now)
    {
        yield return Record(
            zone,
            ZoneName,
            DnsResourceRecordTypeEnum.Soa,
            new SoaRecordData(
                DnsName.Parse($"ns1.{ZoneName}"),
                DnsName.Parse($"hostmaster.{ZoneName}"),
                1,
                3600,
                600,
                86400,
                60
            ),
            now
        );
        yield return Record(
            zone,
            ZoneName,
            DnsResourceRecordTypeEnum.NS,
            new NsRecordData(DnsName.Parse($"ns1.{ZoneName}")),
            now
        );
        yield return Record(
            zone,
            $"ns1.{ZoneName}",
            DnsResourceRecordTypeEnum.A,
            new ARecordData(IPAddress.Parse("192.0.2.53")),
            now
        );

        // 25 names of each of four types: 100 records.
        for (int index = 0; index < 25; index++)
        {
            yield return Record(
                zone,
                HostedName(index),
                DnsResourceRecordTypeEnum.A,
                new ARecordData(IPAddress.Parse($"192.0.2.{index + 1}")),
                now
            );
            yield return Record(
                zone,
                $"aaaa{index}.{ZoneName}",
                DnsResourceRecordTypeEnum.Aaaa,
                new AaaaRecordData(IPAddress.Parse($"2001:db8::{index + 1:x}")),
                now
            );
            yield return Record(
                zone,
                $"cname{index}.{ZoneName}",
                DnsResourceRecordTypeEnum.Cname,
                new CnameRecordData(DnsName.Parse(HostedName(index))),
                now
            );
            yield return Record(
                zone,
                $"txt{index}.{ZoneName}",
                DnsResourceRecordTypeEnum.Txt,
                new TxtRecordData([$"benchmark record {index}"]),
                now
            );
        }
    }

    private static async Task SeedFilteringAsync(
        SeedWriter writer,
        BenchmarkDataSetShape shape,
        DateTimeOffset now
    )
    {
        DnsFilterListEntity List(int index, string name, DnsFilterListKindEnum kind) =>
            new()
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = now,
                Name = name,
                Kind = kind,
                Action = DnsFilterListActionEnum.Block,
                Source = DnsFilterListSourceEnum.Subscribed,
                SubscriptionUrl = $"https://lists.invalid/pipeline-{index}.txt",
                RefreshInterval = TimeSpan.FromDays(1),
                LastFetchedAtUtc = now,
            };

        List<DnsFilterListEntity> lists =
        [
            List(0, "pipeline-list-0", DnsFilterListKindEnum.Domain),
            List(1, "pipeline-list-1", DnsFilterListKindEnum.Domain),
            List(2, "pipeline-response-ips", DnsFilterListKindEnum.ResponseIp),
            List(3, "pipeline-client-sources", DnsFilterListKindEnum.ClientSource),
        ];
        await writer.AddAllAsync<DnsFilterListEntity, IDnsFilterListRepository>("lists", lists);

        IEnumerable<DnsFilterListEntryEntity> Entries()
        {
            DnsFilterListEntryEntity Entry(
                DnsFilterListEntity list,
                DnsFilterEntryKindEnum kind,
                string value,
                bool isException = false
            ) =>
                new()
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    ListId = list.Id,
                    Kind = kind,
                    IsException = isException,
                    Value = value,
                };

            for (int list = 0; list < 2; list++)
            {
                for (int entry = 0; entry < shape.EntriesPerList; entry++)
                {
                    // The shapes of BenchmarkDataSet: 5 % wildcard or regex, 1 % exceptions.
                    DnsFilterEntryKindEnum kind = (entry % 40) switch
                    {
                        0 => DnsFilterEntryKindEnum.Wildcard,
                        1 => DnsFilterEntryKindEnum.Regex,
                        _ => DnsFilterEntryKindEnum.Domain,
                    };
                    yield return Entry(
                        lists[list],
                        kind,
                        kind switch
                        {
                            DnsFilterEntryKindEnum.Wildcard =>
                                $"*.wild{entry}.list{list}.blocked.test",
                            DnsFilterEntryKindEnum.Regex =>
                                $"^rx{entry}\\.list{list}\\.blocked\\.test$",
                            _ => BenchmarkDataSet.FilterEntryValue(list, entry),
                        },
                        entry % 100 == 7
                    );
                }
            }

            // The CIDRs sit in ranges no answer and no client uses, so the lists are scanned and
            // never match.
            for (int index = 0; index < 100; index++)
                yield return Entry(lists[2], DnsFilterEntryKindEnum.Domain, $"198.18.{index}.0/24");

            for (int index = 0; index < 50; index++)
                yield return Entry(lists[3], DnsFilterEntryKindEnum.Domain, $"100.64.{index}.0/24");
        }

        await writer.AddAllAsync<DnsFilterListEntryEntity, IDnsFilterListEntryRepository>(
            "list entries",
            Entries()
        );

        DnsClientGroupEntity defaultGroup = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = now,
            Name = "default",
            IsDefault = true,
        };
        DnsClientGroupEntity group = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = now,
            Name = "pipeline-group",
        };
        await writer.AddAllAsync<DnsClientGroupEntity, IDnsClientGroupRepository>(
            "groups",
            [defaultGroup, group]
        );

        await writer.AddAllAsync<DnsClientGroupNetworkEntity, IDnsClientGroupNetworkRepository>(
            "group networks",
            new[] { "10.1.0.0/16", "192.168.1.0/24" }.Select(cidr => new DnsClientGroupNetworkEntity
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = now,
                ClientGroupId = group.Id,
                Cidr = cidr,
            })
        );

        await writer.AddAllAsync<
            DnsClientGroupListAssignmentEntity,
            IDnsClientGroupListAssignmentRepository
        >(
            "group assignments",
            new[] { defaultGroup, group }.SelectMany(target =>
                lists.Select(list => new DnsClientGroupListAssignmentEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    ClientGroupId = target.Id,
                    ListId = list.Id,
                })
            )
        );

        DnsClientEntity client = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = now,
            DisplayName = "Pipeline client",
            ClientGroupId = group.Id,
        };
        await writer.AddAllAsync<DnsClientEntity, IDnsClientRepository>("clients", [client]);
        await writer.AddAllAsync<DnsClientAddressEntity, IDnsClientAddressRepository>(
            "client addresses",
            [
                new DnsClientAddressEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    ClientId = client.Id,
                    ClientIdentifier = BenchmarkDataSet.ClientIdentifier(2),
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    Address = BenchmarkDataSet.ClientAddress(2),
                    FirstSeenUtc = now,
                    LastSeenUtc = now,
                },
            ]
        );
    }

    private static async Task SeedRewritesAndForwarderAsync(SeedWriter writer, DateTimeOffset now)
    {
        await writer.AddAllAsync<DnsRewriteEntity, IDnsRewriteRepository>(
            "rewrites",
            Enumerable
                .Range(0, RewriteCount)
                .Select(index => new DnsRewriteEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    Domain = RewriteName(index),
                    Target = $"192.0.2.{index % 250 + 1}",
                })
        );

        // The port is a placeholder: the host rewrites it to the fake upstream's port.
        await writer.AddAllAsync<DnsForwarderEntity, IDnsForwarderRepository>(
            "forwarder",
            [
                new DnsForwarderEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    Name = "pipeline-upstream",
                    DefinitionString = "127.0.0.1:53",
                    SortOrder = 0,
                },
            ]
        );
    }
}
