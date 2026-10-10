using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EngineHosts;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.MongoDb;
using Ninlil.Dns.Data.MongoDb.Maintenance;
using Ninlil.Dns.Identity;

// Define our namespace.
namespace Ninlil.Benchmarks.Seeding;

/// <summary>
///     This class generates and writes the deterministic data set the benchmarks run on: a
///     busy home or small-office installation at <see cref="BenchmarkScaleEnum.Large" /> (a
///     million query-log entries, a thousand zones, three 100,000-entry filter lists and so on),
///     and a hundredth of it at <see cref="BenchmarkScaleEnum.Small" />. Every value comes from
///     one seeded <see cref="Random" />, so a database seeded today equals one seeded tomorrow.
///     <para>
///         <c>Program</c> seeds one database per engine and scale, named by
///         <see cref="DatabaseName" />, through the repositories. The last row written is a
///         marker zone (<see cref="MarkerZoneName" />); a database that has it is complete and
///         is not seeded again, which lets a rerun, a reused container or a server given with
///         <c>--connection</c> skip the long seed. Change <see cref="Version" /> whenever the
///         generator changes, so old databases are not mistaken for new ones.
///     </para>
/// </summary>
public static class BenchmarkDataSet
{
    /// <summary>
    ///     This constant contains the version of the generator; it is part of the database name
    ///     and of the marker zone name.
    /// </summary>
    public const int Version = 4;

    /// <summary>
    ///     This constant contains the seed of the random generator.
    /// </summary>
    public const int RandomSeed = 20261009;

    /// <summary>
    ///     This constant contains the apex of the one zone with many records.
    /// </summary>
    public const string BigZoneName = "big.bench.";

    /// <summary>
    ///     This constant contains the name of the one webhook subscription that owns the
    ///     delivery attempts.
    /// </summary>
    public const string WebhookSubscriptionName = "bench-subscription";

    /// <summary>
    ///     This constant contains the name of the DHCP scope prefix; scope n is
    ///     <c>bench-scope-n</c>.
    /// </summary>
    public const string ScopeNamePrefix = "bench-scope-";

    /// <summary>
    ///     This constant contains the length of the time range the query log covers.
    /// </summary>
    public static readonly TimeSpan QueryLogSpan = TimeSpan.FromDays(7);

    /// <summary>
    ///     This property contains the name of the marker zone that is written last.
    /// </summary>
    public static string MarkerZoneName => $"seed-complete-v{Version}.bench.";

    /// <summary>
    ///     This method returns the counts of the data set at <paramref name="scale" />.
    /// </summary>
    public static BenchmarkDataSetShape ShapeFor(BenchmarkScaleEnum scale) =>
        scale == BenchmarkScaleEnum.Large
            ? new BenchmarkDataSetShape(
                QueryLogEntries: 1_000_000,
                QueryLogClientAddresses: 300,
                QueryLogClientIdentifiers: 200,
                QueryLogNames: 50_000,
                Zones: 1_000,
                RecordsPerZone: 100,
                BigZoneRecords: 50_000,
                PtrRecords: 200,
                FilterLists: 3,
                EntriesPerList: 100_000,
                LocalListEntries: 1_000,
                ClientGroups: 5,
                Clients: 2_000,
                ClientAddresses: 3_000,
                ClientSchedules: 20,
                Rewrites: 200,
                ForwarderZones: 3,
                GlobalForwarders: 4,
                DhcpScopes: 4,
                Leases: 5_000,
                Reservations: 200,
                WebhookAttempts: 50_000,
                Users: 50
            )
            : new BenchmarkDataSetShape(
                QueryLogEntries: 10_000,
                QueryLogClientAddresses: 30,
                QueryLogClientIdentifiers: 20,
                QueryLogNames: 500,
                Zones: 10,
                RecordsPerZone: 10,
                BigZoneRecords: 500,
                PtrRecords: 20,
                FilterLists: 3,
                EntriesPerList: 1_000,
                LocalListEntries: 100,
                ClientGroups: 5,
                Clients: 20,
                ClientAddresses: 30,
                ClientSchedules: 5,
                Rewrites: 20,
                ForwarderZones: 3,
                GlobalForwarders: 4,
                DhcpScopes: 4,
                Leases: 200,
                Reservations: 20,
                WebhookAttempts: 500,
                Users: 5
            );

    /// <summary>
    ///     This method returns the name of the database that holds the data set of
    ///     <paramref name="scale" />, the same on every engine.
    /// </summary>
    public static string DatabaseName(BenchmarkScaleEnum scale) =>
        $"ninlil_bench_{scale.ToString().ToLowerInvariant()}_v{Version}";

    /// <summary>
    ///     This method returns the address of query-log client <paramref name="rank" /> (0 is the
    ///     busiest).
    /// </summary>
    public static string ClientAddress(int rank) => $"10.1.{rank / 250}.{rank % 250 + 1}";

    /// <summary>
    ///     This method returns the identifier of client <paramref name="rank" />; the first
    ///     identifiers belong both to query-log clients and to rows of the client directory.
    /// </summary>
    public static string ClientIdentifier(int rank) => $"client-{rank:D5}";

    /// <summary>
    ///     This method returns the queried name of rank <paramref name="rank" />. Names share
    ///     a hundred parent domains (<c>exampleN.test</c>), so a substring search has hits.
    /// </summary>
    public static string QueriedName(int rank) => $"host{rank}.example{rank % 100}.test.";

    /// <summary>
    ///     This method returns the name of ordinary zone <paramref name="index" />.
    /// </summary>
    public static string ZoneName(int index) => $"zone{index:D4}.bench.";

    /// <summary>
    ///     This method returns the name of forwarder zone <paramref name="index" />, without a
    ///     trailing dot: <c>ForwarderQueryResolver</c> trims the dot from the names it looks up,
    ///     so a zone stored with one is never found.
    /// </summary>
    public static string ForwarderZoneName(int index) => $"forward{index}.corp.test";

    /// <summary>
    ///     This method returns the name of subscribed filter list <paramref name="index" />.
    /// </summary>
    public static string FilterListName(int index) => $"bench-list-{index}";

    /// <summary>
    ///     This method returns the name of the local filter list.
    /// </summary>
    public static string LocalFilterListName => "bench-local-list";

    /// <summary>
    ///     This method returns the blocked domain value of filter entry <paramref name="entry" />
    ///     of list <paramref name="list" />.
    /// </summary>
    public static string FilterEntryValue(int list, int entry) =>
        $"ads{entry}.list{list}.blocked.test";

    /// <summary>
    ///     This method returns the user name of user <paramref name="index" />.
    /// </summary>
    public static string UserName(int index) => $"bench-user-{index}";

    /// <summary>
    ///     This method returns whether the database behind <paramref name="database" /> already
    ///     holds a complete data set (the marker zone exists).
    /// </summary>
    public static async Task<bool> IsSeededAsync(EngineDatabase database)
    {
        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IZoneRepository zones = scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        return await zones.FindByNameAsync(MarkerZoneName) != null;
    }

    /// <summary>
    ///     This method creates the schema of a relational database from the real migrations, so
    ///     the benchmarks see the indexes a real installation has. On MongoDB it runs the startup
    ///     guard instead, which creates the indexes the host creates.
    /// </summary>
    public static async Task MigrateAsync(EngineDatabase database)
    {
        NinlilDnsMongoContext mongoContext = database.Services.GetService<NinlilDnsMongoContext>();
        if (mongoContext != null)
        {
            await MongoStartupGuard.RunAsync(mongoContext, NullLogger.Instance);
            return;
        }

        try
        {
            await database.CreateContext().Database.MigrateAsync();
        }
        catch (InvalidOperationException)
        {
            // No EF context is registered: nothing to migrate.
        }
    }

    /// <summary>
    ///     This method seeds the database unless it holds a complete data set already, and
    ///     returns whether it wrote anything.
    /// </summary>
    /// <param name="database">The empty or seeded database.</param>
    /// <param name="scale">The size of the data set.</param>
    /// <param name="log">Where progress lines go.</param>
    public static async Task<bool> EnsureSeededAsync(
        EngineDatabase database,
        BenchmarkScaleEnum scale,
        TextWriter log
    )
    {
        await MigrateAsync(database);

        if (await IsSeededAsync(database))
        {
            await log.WriteLineAsync("  data set present, not seeding again");
            return false;
        }

        BenchmarkDataSetShape shape = ShapeFor(scale);
        Random random = new(RandomSeed);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SeedWriter writer = new(database.Services, log);

        await writer.AddAllAsync<DnsQueryLogEntryEntity, IDnsQueryLogRepository>(
            "query log",
            GenerateQueryLog(shape, random, now)
        );
        await SeedZonesAsync(writer, shape, random, now);
        (List<DnsClientGroupEntity> groups, List<DnsFilterListEntity> lists) =
            await SeedFilterListsAsync(writer, shape, now);
        await SeedClientsAsync(writer, shape, random, now, groups, lists);
        await SeedRewritesAndForwardersAsync(writer, shape, now);
        await SeedDhcpAsync(writer, shape, random, now);
        await SeedWebhooksAsync(writer, shape, random, now);
        await SeedUsersAsync(writer, shape, now);

        // The marker goes last: a seed that stopped half way is seeded again from scratch only
        // after the database is dropped, because the partial rows are still there.
        await writer.AddAllAsync<ZoneEntity, IZoneRepository>(
            "marker",
            [NewZone(MarkerZoneName, now)]
        );

        return true;
    }

    /// <summary>
    ///     This method generates query-log rows over <see cref="QueryLogSpan" /> before
    ///     <paramref name="now" />, oldest first, with time-ordered version 7 ids like the real
    ///     log writes: clients and names follow a Zipf distribution, A queries are
    ///     60 %, AAAA 25 %, HTTPS 8 % and the rest TXT or MX; NoError is 85 %, NXDOMAIN 10 % and
    ///     SERVFAIL 5 %.
    /// </summary>
    public static IEnumerable<DnsQueryLogEntryEntity> GenerateQueryLog(
        BenchmarkDataSetShape shape,
        Random random,
        DateTimeOffset now,
        int? count = null
    )
    {
        ZipfSampler clients = new(shape.QueryLogClientAddresses);
        ZipfSampler identifiers = new(shape.QueryLogClientIdentifiers);
        ZipfSampler names = new(shape.QueryLogNames);

        int total = count ?? shape.QueryLogEntries;

        for (int index = 0; index < total; index++)
        {
            int clientRank = clients.Next(random);
            double typeRoll = random.NextDouble();
            double codeRoll = random.NextDouble();

            // Rows are produced oldest first, one time slice each, with a random instant inside
            // the slice, because a real log is written in time order: row position then follows
            // OccurredAtUtc, and so do the table rows an index on it points at. A random order
            // would make every index-driven read a random read and overstate its cost.
            double slice = QueryLogSpan.Ticks / (double)total;
            DateTimeOffset occurred =
                now
                - QueryLogSpan
                + TimeSpan.FromTicks((long)((index + random.NextDouble()) * slice));

            yield return new DnsQueryLogEntryEntity
            {
                Id = Guid.CreateVersion7(occurred),
                CreatedAtUtc = occurred,
                OccurredAtUtc = occurred,
                ClientAddress = ClientAddress(clientRank),
                Protocol = DnsTransportProtocolEnum.Udp,
                QueriedName = QueriedName(names.Next(random)),
                QueriedType = typeRoll switch
                {
                    < 0.60 => DnsResourceRecordTypeEnum.A,
                    < 0.85 => DnsResourceRecordTypeEnum.Aaaa,
                    < 0.93 => DnsResourceRecordTypeEnum.Https,
                    < 0.97 => DnsResourceRecordTypeEnum.Txt,
                    _ => DnsResourceRecordTypeEnum.MX,
                },
                ResponseCode = codeRoll switch
                {
                    < 0.85 => DnsResponseCodeEnum.NoError,
                    < 0.95 => DnsResponseCodeEnum.NxDomain,
                    _ => DnsResponseCodeEnum.ServerFailure,
                },
                AnswerCount = codeRoll < 0.85 ? 1 + random.Next(3) : 0,
                ClientIdentifier = ClientIdentifier(identifiers.Next(random)),
                ClientProtocolVersion = InternetProtocolVersionEnum.IPv4,
            };
        }
    }

    private static ZoneEntity NewZone(string name, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = now,
            Name = name,
            Type = ZoneTypeEnum.Primary,
        };

    private static async Task SeedZonesAsync(
        SeedWriter writer,
        BenchmarkDataSetShape shape,
        Random random,
        DateTimeOffset now
    )
    {
        List<ZoneEntity> zones = [NewZone(BigZoneName, now)];
        zones.AddRange(Enumerable.Range(0, shape.Zones).Select(i => NewZone(ZoneName(i), now)));
        await writer.AddAllAsync<ZoneEntity, IZoneRepository>("zones", zones);

        RecordEntity Record(
            ZoneEntity zone,
            string name,
            int index,
            RecordManagedByFeatureEnum? managed = null
        ) =>
            new()
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = now,
                ZoneId = zone.Id,
                Name = name,
                Type = DnsResourceRecordTypeEnum.A,
                TimeToLiveSeconds = 300,
                DataJson = $"{{\"Address\":\"192.0.{index / 250 % 250}.{index % 250 + 1}\"}}",
                ManagedByFeature = managed,
            };

        IEnumerable<RecordEntity> Records()
        {
            for (int index = 0; index < shape.BigZoneRecords; index++)
                yield return Record(zones[0], $"rec{index}.{BigZoneName}", index);

            for (int zoneIndex = 1; zoneIndex < zones.Count; zoneIndex++)
            {
                for (int index = 0; index < shape.RecordsPerZone; index++)
                    yield return Record(
                        zones[zoneIndex],
                        $"rec{index}.{zones[zoneIndex].Name}",
                        index
                    );
            }

            // Managed PTR records live in the first ordinary zone (a stand-in for a reverse zone).
            for (int index = 0; index < shape.PtrRecords; index++)
            {
                RecordEntity ptr = Record(
                    zones[1],
                    $"{index + 1}.ptr.{zones[1].Name}",
                    index,
                    RecordManagedByFeatureEnum.Ptr
                );
                ptr.Type = DnsResourceRecordTypeEnum.Ptr;
                yield return ptr;
            }
        }

        await writer.AddAllAsync<RecordEntity, IRecordRepository>("records", Records());
    }

    private static async Task<(
        List<DnsClientGroupEntity> Groups,
        List<DnsFilterListEntity> Lists
    )> SeedFilterListsAsync(SeedWriter writer, BenchmarkDataSetShape shape, DateTimeOffset now)
    {
        List<DnsFilterListEntity> lists =
        [
            .. Enumerable
                .Range(0, shape.FilterLists)
                .Select(i => new DnsFilterListEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    Name = FilterListName(i),
                    Kind = DnsFilterListKindEnum.Domain,
                    Action =
                        i == shape.FilterLists - 1
                            ? DnsFilterListActionEnum.Allow
                            : DnsFilterListActionEnum.Block,
                    Source = DnsFilterListSourceEnum.Subscribed,
                    SubscriptionUrl = $"https://lists.invalid/{i}.txt",
                    RefreshInterval = TimeSpan.FromDays(1),
                    LastFetchedAtUtc = now,
                }),
            new DnsFilterListEntity
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = now,
                Name = LocalFilterListName,
                Kind = DnsFilterListKindEnum.Domain,
                Action = DnsFilterListActionEnum.Block,
                Source = DnsFilterListSourceEnum.Local,
            },
        ];
        await writer.AddAllAsync<DnsFilterListEntity, IDnsFilterListRepository>("lists", lists);

        IEnumerable<DnsFilterListEntryEntity> Entries()
        {
            for (int list = 0; list < lists.Count; list++)
            {
                int count =
                    list < shape.FilterLists ? shape.EntriesPerList : shape.LocalListEntries;

                for (int entry = 0; entry < count; entry++)
                {
                    // 5 % wildcard or regex entries, 1 % exceptions.
                    DnsFilterEntryKindEnum kind = (entry % 40) switch
                    {
                        0 => DnsFilterEntryKindEnum.Wildcard,
                        1 => DnsFilterEntryKindEnum.Regex,
                        _ => DnsFilterEntryKindEnum.Domain,
                    };

                    yield return new DnsFilterListEntryEntity
                    {
                        Id = Guid.NewGuid(),
                        CreatedAtUtc = now,
                        ListId = lists[list].Id,
                        Kind = kind,
                        IsException = entry % 100 == 7,
                        Value = kind switch
                        {
                            DnsFilterEntryKindEnum.Wildcard =>
                                $"*.wild{entry}.list{list}.blocked.test",
                            DnsFilterEntryKindEnum.Regex =>
                                $"^rx{entry}\\.list{list}\\.blocked\\.test$",
                            _ => FilterEntryValue(list, entry),
                        },
                    };
                }
            }
        }

        await writer.AddAllAsync<DnsFilterListEntryEntity, IDnsFilterListEntryRepository>(
            "list entries",
            Entries()
        );

        // Groups, their networks and list assignments: the default group uses the first two
        // lists, group g uses list g % lists.
        List<DnsClientGroupEntity> groups =
        [
            new()
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = now,
                Name = "default",
                IsDefault = true,
            },
            .. Enumerable
                .Range(1, shape.ClientGroups)
                .Select(g => new DnsClientGroupEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    Name = $"group-{g}",
                }),
        ];
        await writer.AddAllAsync<DnsClientGroupEntity, IDnsClientGroupRepository>("groups", groups);

        await writer.AddAllAsync<DnsClientGroupNetworkEntity, IDnsClientGroupNetworkRepository>(
            "group networks",
            groups
                .Skip(1)
                .SelectMany(
                    (group, index) =>
                        new[] { $"10.{index + 1}.0.0/16", $"192.168.{index + 1}.0/24" }.Select(
                            cidr => new DnsClientGroupNetworkEntity
                            {
                                Id = Guid.NewGuid(),
                                CreatedAtUtc = now,
                                ClientGroupId = group.Id,
                                Cidr = cidr,
                            }
                        )
                )
        );

        List<DnsClientGroupListAssignmentEntity> assignments =
        [
            .. new[] { 0, 1 }.Select(list => Assignment(groups[0], lists[list])),
            .. groups
                .Skip(1)
                .Select((group, index) => Assignment(group, lists[index % shape.FilterLists])),
            Assignment(groups[1], lists[^1]),
        ];
        await writer.AddAllAsync<
            DnsClientGroupListAssignmentEntity,
            IDnsClientGroupListAssignmentRepository
        >("group assignments", assignments);

        DnsClientGroupListAssignmentEntity Assignment(
            DnsClientGroupEntity group,
            DnsFilterListEntity list
        ) =>
            new()
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = now,
                ClientGroupId = group.Id,
                ListId = list.Id,
            };

        return (groups, lists);
    }

    private static async Task SeedClientsAsync(
        SeedWriter writer,
        BenchmarkDataSetShape shape,
        Random random,
        DateTimeOffset now,
        List<DnsClientGroupEntity> groups,
        List<DnsFilterListEntity> lists
    )
    {
        // Clients are spread over the groups.
        List<Guid> groupIds = [.. groups.Select(group => group.Id)];

        List<DnsClientEntity> clients =
        [
            .. Enumerable
                .Range(0, shape.Clients)
                .Select(i => new DnsClientEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now - TimeSpan.FromMinutes(i),
                    DisplayName = i % 10 == 0 ? null : $"Device {(i * 7919) % shape.Clients}",
                    ClientGroupId = groupIds[i % groupIds.Count],
                    IsFavorite = i % 25 == 0,
                }),
        ];
        await writer.AddAllAsync<DnsClientEntity, IDnsClientRepository>("clients", clients);

        await writer.AddAllAsync<DnsClientAddressEntity, IDnsClientAddressRepository>(
            "client addresses",
            Enumerable
                .Range(0, shape.ClientAddresses)
                .Select(j => new DnsClientAddressEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    ClientId = clients[j % clients.Count].Id,
                    ClientIdentifier = ClientIdentifier(j),
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    Address = ClientAddress(j % shape.QueryLogClientAddresses),
                    FirstSeenUtc = now - TimeSpan.FromDays(random.Next(1, 30)),
                    LastSeenUtc =
                        j % 7 == 0 ? null : now - TimeSpan.FromMinutes(random.Next(1, 10_000)),
                })
        );

        await writer.AddAllAsync<DnsClientListAssignmentEntity, IDnsClientListAssignmentRepository>(
            "client assignments",
            clients
                .Take(Math.Max(2, shape.Clients / 100))
                .Select(client => new DnsClientListAssignmentEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    ClientId = client.Id,
                    ListId = lists[^2].Id,
                })
        );

        await writer.AddAllAsync<DnsClientScheduleEntity, IDnsClientScheduleRepository>(
            "client schedules",
            clients
                .Take(shape.ClientSchedules)
                .Select(client => new DnsClientScheduleEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    ClientId = client.Id,
                    DaysOfWeek = DnsScheduleDayOfWeekEnum.All,
                    StartTime = new TimeOnly(22, 0),
                    EndTime = new TimeOnly(23, 0),
                    TimeZoneId = "UTC",
                })
        );
    }

    private static async Task SeedRewritesAndForwardersAsync(
        SeedWriter writer,
        BenchmarkDataSetShape shape,
        DateTimeOffset now
    )
    {
        await writer.AddAllAsync<DnsRewriteEntity, IDnsRewriteRepository>(
            "rewrites",
            Enumerable
                .Range(0, shape.Rewrites)
                .Select(i => new DnsRewriteEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    Domain = $"rewrite{i}.home.test",
                    Target = $"192.0.2.{i % 250 + 1}",
                })
        );

        await writer.AddAllAsync<DnsForwarderEntity, IDnsForwarderRepository>(
            "forwarders",
            Enumerable
                .Range(0, shape.GlobalForwarders)
                .Select(i => new DnsForwarderEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    Name = $"forwarder-{i}",
                    DefinitionString = $"192.0.2.{i + 10}",
                    SortOrder = i,
                })
        );

        await writer.AddAllAsync<ZoneEntity, IZoneRepository>(
            "forwarder zones",
            Enumerable
                .Range(0, shape.ForwarderZones)
                .Select(i =>
                {
                    ZoneEntity zone = NewZone(ForwarderZoneName(i), now);
                    zone.Type = ZoneTypeEnum.Forwarder;
                    zone.ForwarderAddresses = [$"192.0.2.{i + 50}"];
                    return zone;
                })
        );
    }

    private static async Task SeedDhcpAsync(
        SeedWriter writer,
        BenchmarkDataSetShape shape,
        Random random,
        DateTimeOffset now
    )
    {
        List<DhcpScopeEntity> scopes =
        [
            .. Enumerable
                .Range(0, shape.DhcpScopes)
                .Select(s => new DhcpScopeEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    Name = $"{ScopeNamePrefix}{s}",
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    RangeStartAddress = $"10.{100 + s}.0.1",
                    RangeEndAddress = $"10.{100 + s}.255.254",
                    SubnetPrefixLength = 16,
                    // The lease durations stay at the entity defaults (24 hours and 7 days).
                    GatewayAddresses = [$"10.{100 + s}.0.1"],
                    DnsServerAddresses = ["192.0.2.53"],
                }),
        ];
        await writer.AddAllAsync<DhcpScopeEntity, IDhcpScopeRepository>("scopes", scopes);

        await writer.AddAllAsync<LeaseEntity, ILeaseRepository>(
            "leases",
            Enumerable
                .Range(0, shape.Leases)
                .Select(n =>
                {
                    int scope = n % shape.DhcpScopes;
                    int host = n / shape.DhcpScopes;
                    bool active = random.NextDouble() < 0.8;
                    return new LeaseEntity
                    {
                        Id = Guid.NewGuid(),
                        CreatedAtUtc = now,
                        ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                        ScopeName = scopes[scope].Name,
                        ClientIdentifier = $"lease-client-{n:D6}",
                        IpAddress = $"10.{100 + scope}.{host / 250 + 1}.{host % 250 + 1}",
                        HostName = $"lease{n}",
                        State = active ? LeaseStateEnum.Active : LeaseStateEnum.Expired,
                        LeaseObtainedAtUtc = now - TimeSpan.FromHours(1),
                        // A fiftieth of the active leases is already past its expiry, so the
                        // sweep query finds rows.
                        LeaseExpiresAtUtc = active
                            ? now
                                + TimeSpan.FromMinutes(random.Next(-60, 1_440))
                                    * (n % 50 == 0 ? 0 : 1)
                            : now - TimeSpan.FromHours(2),
                    };
                })
        );

        await writer.AddAllAsync<DhcpReservationEntity, IDhcpReservationRepository>(
            "reservations",
            Enumerable
                .Range(0, shape.Reservations)
                .Select(n =>
                {
                    int scope = n % shape.DhcpScopes;
                    return new DhcpReservationEntity
                    {
                        Id = Guid.NewGuid(),
                        CreatedAtUtc = now,
                        ScopeId = scopes[scope].Id,
                        ClientIdentifier = $"reserved-client-{n:D5}",
                        IpAddress = $"10.{100 + scope}.250.{n / shape.DhcpScopes % 250 + 1}",
                        HostName = $"reserved{n}",
                    };
                })
        );
    }

    private static async Task SeedWebhooksAsync(
        SeedWriter writer,
        BenchmarkDataSetShape shape,
        Random random,
        DateTimeOffset now
    )
    {
        WebhookSubscriptionEntity subscription = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = now,
            Name = WebhookSubscriptionName,
            TargetUrl = "https://hooks.invalid/bench",
            EventTypeNames = ["DnsCacheEvicted"],
        };
        await writer.AddAllAsync<WebhookSubscriptionEntity, IWebhookSubscriptionRepository>(
            "webhook subscription",
            [subscription]
        );

        await writer.AddAllAsync<WebhookDeliveryAttemptEntity, IWebhookDeliveryAttemptRepository>(
            "webhook attempts",
            Enumerable
                .Range(0, shape.WebhookAttempts)
                .Select(n =>
                {
                    DateTimeOffset created =
                        now - TimeSpan.FromSeconds(random.Next(0, 30 * 86_400));
                    return new WebhookDeliveryAttemptEntity
                    {
                        Id = Guid.NewGuid(),
                        CreatedAtUtc = created,
                        SubscriptionId = subscription.Id,
                        SubscriptionName = subscription.Name,
                        EventTypeName = "DnsCacheEvicted",
                        PayloadJson = "{\"evicted\":42}",
                        Status = WebhookDeliveryAttemptStatusEnum.Succeeded,
                        AttemptCount = 1,
                        MaxAttempts = 5,
                        LastAttemptedAtUtc = created,
                        LastHttpStatusCode = 200,
                        CompletedAtUtc = created,
                    };
                })
        );
    }

    private static async Task SeedUsersAsync(
        SeedWriter writer,
        BenchmarkDataSetShape shape,
        DateTimeOffset now
    )
    {
        List<RoleEntity> roles =
        [
            .. Enumerable
                .Range(0, 3)
                .Select(r => new RoleEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    Name = $"bench-role-{r}",
                    NormalizedName = $"BENCH-ROLE-{r}",
                }),
        ];
        await writer.AddAllAsync<RoleEntity, IRoleRepository>("roles", roles);

        await writer.InScopeAsync(async provider =>
        {
            IRoleRepository repository = provider.GetRequiredService<IRoleRepository>();

            foreach (RoleEntity role in roles)
            {
                for (int claim = 0; claim < 10; claim++)
                    await repository.AddClaimAsync(
                        role.Id,
                        NinlilDnsClaimTypes.Permission,
                        $"bench.{role.Name}.permission{claim}"
                    );
            }
        });

        for (int index = 0; index < shape.Users; index++)
        {
            await writer.InScopeAsync(async provider =>
            {
                IUserRepository repository = provider.GetRequiredService<IUserRepository>();
                UserEntity user = new()
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    UserName = UserName(index),
                    NormalizedUserName = UserName(index).ToUpperInvariant(),
                    Email = $"{UserName(index)}@bench.test",
                    NormalizedEmail = $"{UserName(index)}@bench.test".ToUpperInvariant(),
                    EmailConfirmed = true,
                    SecurityStamp = Guid.NewGuid().ToString("N"),
                    ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                };
                await repository.AddAsync(user);
                await provider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

                foreach (RoleEntity role in roles)
                    await repository.AddToRoleAsync(user.Id, role.Id);

                await repository.AddClaimsAsync(
                    user.Id,
                    [(NinlilDnsClaimTypes.Permission, "bench.user.permission")]
                );
            });
        }
    }
}
