using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class covers <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnsClientRepository" />/
///     <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnsClientAddressRepository" />'s
///     real round trip, the <c>GetByClientIdentifierAsync</c> join across both tables, and real,
///     pushed-down <c>SearchAsync</c>/<c>CountAsync</c> pagination against a real SQLite-backed
///     database -- the same proof shape <see cref="ClientGroupRepositorySearchTests" /> already
///     establishes for the closest sibling entity.
/// </summary>
public sealed class DnsClientRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    [Fact]
    public async Task AddAsync_ThenGetByIdAsync_RoundTripsEveryField()
    {
        Guid clientId = Guid.NewGuid();
        Guid clientGroupId = Guid.NewGuid();

        await using (AsyncServiceScope writeScope = _testHost.Host.Services.CreateAsyncScope())
        {
            IDnsClientRepository repository =
                writeScope.ServiceProvider.GetRequiredService<IDnsClientRepository>();
            IDnsClientAddressRepository addressRepository =
                writeScope.ServiceProvider.GetRequiredService<IDnsClientAddressRepository>();
            IUnitOfWork unitOfWork = writeScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await repository.AddAsync(
                new DnsClientEntity
                {
                    Id = clientId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    DisplayName = "Living Room TV",
                    ClientGroupId = clientGroupId,
                    IsDisabled = false,
                    Comments = "Seeded by a real round-trip test.",
                    DeviceIconKey = DnsClientDeviceIconEnum.Television,
                    IsFavorite = true,
                }
            );
            await addressRepository.AddAsync(
                new DnsClientAddressEntity
                {
                    Id = Guid.NewGuid(),
                    ClientId = clientId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ClientIdentifier = "aa:bb:cc:dd:ee:ff",
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    FirstSeenUtc = DateTimeOffset.UtcNow,
                }
            );
            await unitOfWork.SaveChangesAsync();
        }

        // Read back from a fresh scope, so the assertions prove a real round trip through SQLite
        // rather than merely returning the same tracked in-memory instance just written.
        await using AsyncServiceScope readScope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientRepository readRepository =
            readScope.ServiceProvider.GetRequiredService<IDnsClientRepository>();
        IDnsClientAddressRepository readAddressRepository =
            readScope.ServiceProvider.GetRequiredService<IDnsClientAddressRepository>();

        DnsClientEntity client = await readRepository.GetByIdAsync(clientId);
        IReadOnlyList<DnsClientAddressEntity> addresses =
            await readAddressRepository.ListByClientIdAsync(clientId);

        Assert.NotNull(client);
        Assert.Equal("Living Room TV", client.DisplayName);
        Assert.Equal(clientGroupId, client.ClientGroupId);
        Assert.False(client.IsDisabled);
        Assert.Equal("Seeded by a real round-trip test.", client.Comments);
        Assert.Equal(DnsClientDeviceIconEnum.Television, client.DeviceIconKey);
        Assert.True(client.IsFavorite);

        Assert.Single(addresses);
        Assert.Equal("aa:bb:cc:dd:ee:ff", addresses[0].ClientIdentifier);
        Assert.Equal(InternetProtocolVersionEnum.IPv4, addresses[0].ProtocolVersion);
    }

    [Fact]
    public async Task GetByClientIdentifierAsync_DisambiguatesByProtocolVersion()
    {
        const string sharedIdentifier = "aa:aa:aa:aa:aa:aa";

        await using (AsyncServiceScope writeScope = _testHost.Host.Services.CreateAsyncScope())
        {
            IDnsClientRepository repository =
                writeScope.ServiceProvider.GetRequiredService<IDnsClientRepository>();
            IDnsClientAddressRepository addressRepository =
                writeScope.ServiceProvider.GetRequiredService<IDnsClientAddressRepository>();
            IUnitOfWork unitOfWork = writeScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            // Two devices each have one address sharing the exact same raw identifier text but
            // belonging to two different, disjoint identifier spaces -- proving
            // (ClientIdentifier, ProtocolVersion) is what actually disambiguates a lookup, not
            // ClientIdentifier alone.
            Guid ipv4ClientId = Guid.NewGuid();
            Guid ipv6ClientId = Guid.NewGuid();

            await repository.AddAsync(
                new DnsClientEntity
                {
                    Id = ipv4ClientId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    DisplayName = "IPv4 row",
                }
            );
            await repository.AddAsync(
                new DnsClientEntity
                {
                    Id = ipv6ClientId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    DisplayName = "IPv6 row",
                }
            );
            await addressRepository.AddAsync(
                new DnsClientAddressEntity
                {
                    Id = Guid.NewGuid(),
                    ClientId = ipv4ClientId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ClientIdentifier = sharedIdentifier,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    FirstSeenUtc = DateTimeOffset.UtcNow,
                }
            );
            await addressRepository.AddAsync(
                new DnsClientAddressEntity
                {
                    Id = Guid.NewGuid(),
                    ClientId = ipv6ClientId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ClientIdentifier = sharedIdentifier,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv6,
                    FirstSeenUtc = DateTimeOffset.UtcNow,
                }
            );
            await unitOfWork.SaveChangesAsync();
        }

        await using AsyncServiceScope readScope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientRepository readRepository =
            readScope.ServiceProvider.GetRequiredService<IDnsClientRepository>();

        DnsClientEntity ipv4Client = await readRepository.GetByClientIdentifierAsync(
            sharedIdentifier,
            InternetProtocolVersionEnum.IPv4
        );
        DnsClientEntity ipv6Client = await readRepository.GetByClientIdentifierAsync(
            sharedIdentifier,
            InternetProtocolVersionEnum.IPv6
        );

        Assert.NotNull(ipv4Client);
        Assert.Equal("IPv4 row", ipv4Client.DisplayName);
        Assert.NotNull(ipv6Client);
        Assert.Equal("IPv6 row", ipv6Client.DisplayName);
    }

    [Fact]
    public async Task GetByClientIdentifierAsync_WithNoMatchingRow_ReturnsNull()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientRepository>();

        DnsClientEntity result = await repository.GetByClientIdentifierAsync(
            "00:00:00:00:00:01",
            InternetProtocolVersionEnum.IPv4
        );

        Assert.Null(result);
    }

    /// <summary>
    ///     This method seeds five clients, each with its own single address, with a deliberately
    ///     increasing <c>CreatedAtUtc</c> so a passing ordering assertion can only be explained by
    ///     a real <c>ORDER BY</c> translated at the database, not by insertion order happening to
    ///     already match.
    /// </summary>
    /// <returns>The five seeded clients' own identifiers, in the order they should be returned.</returns>
    private async Task<Guid[]> SeedAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientRepository>();
        IDnsClientAddressRepository addressRepository =
            scope.ServiceProvider.GetRequiredService<IDnsClientAddressRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        string[] identifiersInSeedOrder =
        [
            "dd:dd:dd:dd:dd:dd",
            "bb:bb:bb:bb:bb:bb",
            "ee:ee:ee:ee:ee:ee",
            "aa:aa:aa:aa:aa:aa",
            "cc:cc:cc:cc:cc:cc",
        ];

        List<Guid> clientIds = [];
        DateTimeOffset createdAtUtc = DateTimeOffset.UtcNow;

        foreach (string identifier in identifiersInSeedOrder)
        {
            Guid clientId = Guid.NewGuid();
            clientIds.Add(clientId);

            await repository.AddAsync(
                new DnsClientEntity { Id = clientId, CreatedAtUtc = createdAtUtc }
            );
            await addressRepository.AddAsync(
                new DnsClientAddressEntity
                {
                    Id = Guid.NewGuid(),
                    ClientId = clientId,
                    CreatedAtUtc = createdAtUtc,
                    ClientIdentifier = identifier,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    FirstSeenUtc = createdAtUtc,
                }
            );

            // Each client is created a tick later than the last, so CreatedAtUtc ordering is
            // unambiguous and genuinely different from the seed order above.
            createdAtUtc = createdAtUtc.AddSeconds(1);
        }

        await unitOfWork.SaveChangesAsync();

        // The five clients were seeded in "d, b, e, a, c" order, so returning them in that same
        // order (their real CreatedAtUtc ordering) lets a caller assert against it directly.
        return [.. clientIds];
    }

    [Fact]
    public async Task SearchAsync_ReturnsEveryClientOrderedByCreationTimeRegardlessOfSeedOrder()
    {
        Guid[] expectedOrder = await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientRepository>();

        IReadOnlyList<DnsClientEntity> results = await repository.SearchAsync(
            DnsClientSearchCriteria.All,
            skip: 0,
            take: 50
        );

        Assert.Equal(5, results.Count);
        Assert.Equal(expectedOrder.ToList(), results.Select(client => client.Id).ToList());
    }

    [Fact]
    public async Task SearchAsync_Paginated_ReturnsTheCorrectSliceForEachPage()
    {
        Guid[] expectedOrder = await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientRepository>();

        IReadOnlyList<DnsClientEntity> firstPage = await repository.SearchAsync(
            DnsClientSearchCriteria.All,
            skip: 0,
            take: 2
        );
        IReadOnlyList<DnsClientEntity> secondPage = await repository.SearchAsync(
            DnsClientSearchCriteria.All,
            skip: 2,
            take: 2
        );
        IReadOnlyList<DnsClientEntity> thirdPage = await repository.SearchAsync(
            DnsClientSearchCriteria.All,
            skip: 4,
            take: 2
        );

        Assert.Equal(expectedOrder[..2].ToList(), firstPage.Select(client => client.Id).ToList());
        Assert.Equal(expectedOrder[2..4].ToList(), secondPage.Select(client => client.Id).ToList());
        Assert.Equal(expectedOrder[4..].ToList(), thirdPage.Select(client => client.Id).ToList());

        // No two pages ever overlap.
        Assert.DoesNotContain(secondPage, client => firstPage.Any(first => first.Id == client.Id));
        Assert.DoesNotContain(
            thirdPage,
            client => secondPage.Any(second => second.Id == client.Id)
        );
    }

    [Fact]
    public async Task CountAsync_ReflectsTheFullClientCountRegardlessOfPageSize()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientRepository>();

        int count = await repository.CountAsync(DnsClientSearchCriteria.All);
        IReadOnlyList<DnsClientEntity> onePage = await repository.SearchAsync(
            DnsClientSearchCriteria.All,
            skip: 0,
            take: 2
        );

        Assert.Equal(5, count);
        Assert.Equal(2, onePage.Count);
    }

    [Fact]
    public async Task SearchAsync_WithAFavoritesFilter_PushesItDownForBothThePageAndTheCount()
    {
        Guid[] seeded = await SeedAsync();

        // Star two of the five seeded clients.
        await using (AsyncServiceScope writeScope = _testHost.Host.Services.CreateAsyncScope())
        {
            IDnsClientRepository writeRepository =
                writeScope.ServiceProvider.GetRequiredService<IDnsClientRepository>();
            foreach (Guid id in new[] { seeded[1], seeded[3] })
            {
                DnsClientEntity client = await writeRepository.GetByIdAsync(id);
                client.IsFavorite = true;
                writeRepository.Update(client);
            }

            await writeScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientRepository>();

        IReadOnlyList<DnsClientEntity> favorites = await repository.SearchAsync(
            new DnsClientSearchCriteria(IsFavorite: true),
            skip: 0,
            take: 50
        );
        IReadOnlyList<DnsClientEntity> others = await repository.SearchAsync(
            new DnsClientSearchCriteria(IsFavorite: false),
            skip: 0,
            take: 50
        );

        Assert.Equal([seeded[1], seeded[3]], favorites.Select(client => client.Id).ToList());
        Assert.Equal(3, others.Count);
        Assert.Equal(2, await repository.CountAsync(new DnsClientSearchCriteria(IsFavorite: true)));
        Assert.Equal(
            3,
            await repository.CountAsync(new DnsClientSearchCriteria(IsFavorite: false))
        );
        Assert.Equal(5, await repository.CountAsync(DnsClientSearchCriteria.All));
    }

    /// <summary>
    ///     This method seeds four devices with deliberately different presence histories, relative
    ///     to a fixed <paramref name="now" />: a dual-stack device whose newer sighting is on its
    ///     second address, a device seen long ago, a device never seen at all, and a device seen
    ///     recently -- created in that order, so every ordering below differs from creation order.
    /// </summary>
    /// <returns>The four devices' identifiers, in creation order.</returns>
    private async Task<(Guid DualStack, Guid Stale, Guid Never, Guid Recent)> SeedPresenceAsync(
        DateTimeOffset now
    )
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientRepository>();
        IDnsClientAddressRepository addressRepository =
            scope.ServiceProvider.GetRequiredService<IDnsClientAddressRepository>();

        (Guid, Guid, Guid, Guid) ids = (
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid()
        );
        (Guid id, string name, DateTimeOffset createdAtUtc)[] clients =
        [
            (ids.Item1, "laptop", now.AddDays(-4)),
            (ids.Item2, "", now.AddDays(-3)),
            (ids.Item3, "Printer", now.AddDays(-2)),
            (ids.Item4, null, now.AddMinutes(-30)),
        ];
        foreach ((Guid id, string name, DateTimeOffset createdAtUtc) in clients)
            await repository.AddAsync(
                new DnsClientEntity
                {
                    Id = id,
                    DisplayName = name,
                    CreatedAtUtc = createdAtUtc,
                }
            );

        int suffix = 0;
        async Task AddAddressAsync(
            Guid clientId,
            InternetProtocolVersionEnum version,
            DateTimeOffset? lastSeenUtc
        ) =>
            await addressRepository.AddAsync(
                new DnsClientAddressEntity
                {
                    Id = Guid.NewGuid(),
                    ClientId = clientId,
                    CreatedAtUtc = now.AddDays(-5),
                    ClientIdentifier = $"02:00:00:00:00:{++suffix:x2}",
                    ProtocolVersion = version,
                    FirstSeenUtc = now.AddDays(-5),
                    LastSeenUtc = lastSeenUtc,
                }
            );

        await AddAddressAsync(ids.Item1, InternetProtocolVersionEnum.IPv4, now.AddHours(-6));
        await AddAddressAsync(ids.Item1, InternetProtocolVersionEnum.IPv6, now.AddMinutes(-1));
        await AddAddressAsync(ids.Item2, InternetProtocolVersionEnum.IPv4, now.AddDays(-2));
        await AddAddressAsync(ids.Item3, InternetProtocolVersionEnum.IPv4, null);
        await AddAddressAsync(ids.Item4, InternetProtocolVersionEnum.IPv4, now.AddMinutes(-5));

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return ids;
    }

    [Fact]
    public async Task SearchAsync_WithPresenceFilters_SplitsClientsAtTheCutoffForBothThePageAndTheCount()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        (Guid dualStack, Guid stale, Guid never, Guid recent) = await SeedPresenceAsync(now);

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientRepository>();

        // A ten-minute window: the dual-stack device counts as online through its IPv6 address
        // alone, even though its IPv4 address was last seen hours ago.
        DateTimeOffset cutoff = now.AddMinutes(-10);
        DnsClientSearchCriteria online = new(SeenSinceUtc: cutoff);
        DnsClientSearchCriteria offline = new(NotSeenSinceUtc: cutoff);

        IReadOnlyList<DnsClientEntity> onlinePage = await repository.SearchAsync(online, 0, 50);
        IReadOnlyList<DnsClientEntity> offlinePage = await repository.SearchAsync(offline, 0, 50);

        Assert.Equal([dualStack, recent], onlinePage.Select(client => client.Id).ToList());
        Assert.Equal([stale, never], offlinePage.Select(client => client.Id).ToList());
        Assert.Equal(2, await repository.CountAsync(online));
        Assert.Equal(2, await repository.CountAsync(offline));
    }

    [Fact]
    public async Task CountAsync_WithACreatedSinceFilter_CountsOnlyNewerClients()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        (_, _, _, Guid recent) = await SeedPresenceAsync(now);

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientRepository>();

        DnsClientSearchCriteria createdToday = new(CreatedSinceUtc: now.AddHours(-24));

        Assert.Equal(1, await repository.CountAsync(createdToday));
        Assert.Equal(
            [recent],
            (await repository.SearchAsync(createdToday, 0, 50)).Select(client => client.Id).ToList()
        );
    }

    [Fact]
    public async Task SearchAsync_SortedByLastSeen_PutsTheNewestSightingFirstAndNeverSeenClientsLast()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        (Guid dualStack, Guid stale, Guid never, Guid recent) = await SeedPresenceAsync(now);

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientRepository>();
        DnsClientSearchCriteria byLastSeen = new(SortOrder: DnsClientSortOrderEnum.LastSeen);

        IReadOnlyList<DnsClientEntity> all = await repository.SearchAsync(byLastSeen, 0, 50);
        IReadOnlyList<DnsClientEntity> secondPage = await repository.SearchAsync(byLastSeen, 2, 2);

        // The dual-stack device's newest sighting (its IPv6 address, one minute ago) is what
        // ranks it, not its older IPv4 one.
        Assert.Equal([dualStack, recent, stale, never], all.Select(client => client.Id).ToList());
        Assert.Equal([stale, never], secondPage.Select(client => client.Id).ToList());
    }

    [Fact]
    public async Task SearchAsync_SortedByDisplayName_PutsUnnamedClientsLastInCreationOrder()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        (Guid dualStack, Guid stale, Guid never, Guid recent) = await SeedPresenceAsync(now);

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientRepository>();

        IReadOnlyList<DnsClientEntity> results = await repository.SearchAsync(
            new DnsClientSearchCriteria(SortOrder: DnsClientSortOrderEnum.DisplayName),
            0,
            50
        );

        // "laptop" before "Printer" (case-insensitively, though a binary collation would reverse them), then the empty-named and null-named devices in creation order.
        Assert.Equal(
            [dualStack, never, stale, recent],
            results.Select(client => client.Id).ToList()
        );
    }

    [Fact]
    public async Task SearchAsync_WithSkipBeyondTheEnd_ReturnsAnEmptyPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientRepository>();

        IReadOnlyList<DnsClientEntity> results = await repository.SearchAsync(
            DnsClientSearchCriteria.All,
            skip: 100,
            take: 50
        );

        Assert.Empty(results);
    }

    [Fact]
    public async Task AddAsync_WithADuplicateClientIdentifierAndProtocolVersion_ViolatesTheUniqueIndex()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsClientRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsClientRepository>();
        IDnsClientAddressRepository addressRepository =
            scope.ServiceProvider.GetRequiredService<IDnsClientAddressRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Guid firstClientId = Guid.NewGuid();
        Guid secondClientId = Guid.NewGuid();

        await repository.AddAsync(
            new DnsClientEntity { Id = firstClientId, CreatedAtUtc = DateTimeOffset.UtcNow }
        );
        await repository.AddAsync(
            new DnsClientEntity { Id = secondClientId, CreatedAtUtc = DateTimeOffset.UtcNow }
        );
        await addressRepository.AddAsync(
            new DnsClientAddressEntity
            {
                Id = Guid.NewGuid(),
                ClientId = firstClientId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ClientIdentifier = "ff:ff:ff:ff:ff:ff",
                ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                FirstSeenUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        await addressRepository.AddAsync(
            new DnsClientAddressEntity
            {
                Id = Guid.NewGuid(),
                ClientId = secondClientId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ClientIdentifier = "ff:ff:ff:ff:ff:ff",
                ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                FirstSeenUtc = DateTimeOffset.UtcNow,
            }
        );

        await Assert.ThrowsAnyAsync<Exception>(() => unitOfWork.SaveChangesAsync());
    }
}
