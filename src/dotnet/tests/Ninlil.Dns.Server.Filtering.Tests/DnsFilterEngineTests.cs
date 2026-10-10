using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Filtering.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering.Tests;

/// <summary>
///     This class covers <see cref="DnsFilterEngine" />: which lists apply to a client (CIDR
///     groups, the default group, the client registry and direct assignments), domain, wildcard
///     and regex matching, allow/block precedence, client-source and response-IP lists, denied
///     responses, regex safety, and the live <see cref="DnsFilterOptions.Enable" /> switch. Each
///     test builds the engine over the in-memory repositories in a small service provider (see
///     <see cref="Fixture" />). Exceptions and CNAME cloaking are in the other part of this class.
/// </summary>
public partial class DnsFilterEngineTests
{
    internal sealed class Fixture
    {
        public readonly InMemoryDnsFilterListRepository Lists = new();
        public readonly InMemoryDnsFilterListEntryRepository Entries = new();
        public readonly InMemoryDnsClientGroupRepository Groups = new();
        public readonly InMemoryDnsClientGroupNetworkRepository Networks = new();
        public readonly InMemoryDnsClientGroupListAssignmentRepository Assignments = new();
        public readonly InMemoryDnsClientRepository Clients = new();
        public readonly InMemoryDnsClientAddressRepository ClientAddresses = new();
        public readonly InMemoryDnsClientListAssignmentRepository ClientListAssignments = new();
        public readonly DnsFilterOptions Options = new() { Enable = true };
        public readonly IServiceScopeFactory ScopeFactory;
        public readonly DnsFilterEngine Engine;
        public readonly EntityChangeNotifier Notifier = new();
        public readonly DnsFilterConfigurationCache Cache;
        public readonly DnsClientDirectory Directory;

        /// <param name="identityResolver">
        ///     The optional client identity resolver to build the engine with.
        ///     <see langword="null" /> (the default) leaves the client-registry path off.
        /// </param>
        /// <param name="registerClientRepositories">
        ///     Whether <see cref="Clients" /> and <see cref="ClientListAssignments" /> are registered
        ///     in the engine's service provider. <see langword="false" /> sets up a resolver
        ///     without the client repositories.
        /// </param>
        public Fixture(
            IClientIdentityResolver identityResolver = null,
            bool registerClientRepositories = true
        )
        {
            ServiceCollection services = new();
            services.AddSingleton<Ninlil.Dns.Data.Abstractions.Repository.IDnsFilterListRepository>(
                Lists
            );
            services.AddSingleton<Ninlil.Dns.Data.Abstractions.Repository.IDnsFilterListEntryRepository>(
                Entries
            );
            services.AddSingleton<Ninlil.Dns.Data.Abstractions.Repository.IDnsClientGroupRepository>(
                Groups
            );
            services.AddSingleton<Ninlil.Dns.Data.Abstractions.Repository.IDnsClientGroupNetworkRepository>(
                Networks
            );
            services.AddSingleton<Ninlil.Dns.Data.Abstractions.Repository.IDnsClientGroupListAssignmentRepository>(
                Assignments
            );

            if (registerClientRepositories)
            {
                services.AddSingleton<Ninlil.Dns.Data.Abstractions.Repository.IDnsClientRepository>(
                    Clients
                );
                services.AddSingleton<Ninlil.Dns.Data.Abstractions.Repository.IDnsClientAddressRepository>(
                    ClientAddresses
                );
                services.AddSingleton<Ninlil.Dns.Data.Abstractions.Repository.IDnsClientListAssignmentRepository>(
                    ClientListAssignments
                );
            }

            ScopeFactory = services
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>();
            Cache = new DnsFilterConfigurationCache(
                ScopeFactory,
                Notifier,
                NullLogger<DnsFilterConfigurationCache>.Instance
            );
            Directory = new DnsClientDirectory(
                ScopeFactory,
                Notifier,
                NullLogger<DnsClientDirectory>.Instance
            );
            Engine = new DnsFilterEngine(
                Cache,
                Options2(),
                NullLogger<DnsFilterEngine>.Instance,
                identityResolver,
                registerClientRepositories ? Directory : null
            );
        }

        /// <summary>
        ///     This method stands in for the committed save a real repository write ends with:
        ///     the in-memory repositories have no unit of work, so the fixture's setup helpers
        ///     tell the caches themselves that rows changed.
        /// </summary>
        public void Changed()
        {
            Cache.ExpireAll();
            Directory.Expire();
        }

        private StaticOptionsMonitor<DnsFilterOptions> Options2() => new(Options);

        public DnsClientGroupEntity AddGroup(string name, bool isDefault)
        {
            DnsClientGroupEntity group = AddEntity(
                Groups.Items,
                new DnsClientGroupEntity
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    IsDefault = isDefault,
                }
            );
            Changed();
            return group;
        }

        public void AddNetwork(Guid groupId, string cidr)
        {
            Networks.Items.Add(
                new DnsClientGroupNetworkEntity
                {
                    Id = Guid.NewGuid(),
                    ClientGroupId = groupId,
                    Cidr = cidr,
                }
            );
            Changed();
        }

        public void AddClientIdNetwork(Guid groupId, string clientId)
        {
            Networks.Items.Add(
                new DnsClientGroupNetworkEntity
                {
                    Id = Guid.NewGuid(),
                    ClientGroupId = groupId,
                    ClientId = clientId,
                }
            );
            Changed();
        }

        public DnsFilterListEntity AddList(
            string name,
            DnsFilterListKindEnum kind,
            DnsFilterListActionEnum action,
            params string[] values
        )
        {
            DnsFilterListEntity list = AddEntity(
                Lists.Items,
                new DnsFilterListEntity
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Kind = kind,
                    Action = action,
                    Source = DnsFilterListSourceEnum.Local,
                }
            );

            foreach (string value in values)
                Entries.Items.Add(
                    new DnsFilterListEntryEntity
                    {
                        Id = Guid.NewGuid(),
                        ListId = list.Id,
                        Value = value,
                    }
                );

            Changed();
            return list;
        }

        public DnsFilterListEntity AddListWithKindedEntry(
            string name,
            DnsFilterListActionEnum action,
            DnsFilterEntryKindEnum entryKind,
            string value
        )
        {
            DnsFilterListEntity list = AddEntity(
                Lists.Items,
                new DnsFilterListEntity
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Kind = DnsFilterListKindEnum.Domain,
                    Action = action,
                    Source = DnsFilterListSourceEnum.Local,
                }
            );

            Entries.Items.Add(
                new DnsFilterListEntryEntity
                {
                    Id = Guid.NewGuid(),
                    ListId = list.Id,
                    Value = value,
                    Kind = entryKind,
                }
            );

            Changed();
            return list;
        }

        public void AssignListToGroup(Guid groupId, Guid listId)
        {
            Assignments.Items.Add(
                new DnsClientGroupListAssignmentEntity
                {
                    Id = Guid.NewGuid(),
                    ClientGroupId = groupId,
                    ListId = listId,
                }
            );
            Changed();
        }

        public DnsClientEntity AddClient(
            string clientIdentifier,
            Guid? clientGroupId = null,
            bool isDisabled = false
        )
        {
            DnsClientEntity client = AddEntity(
                Clients.Items,
                new DnsClientEntity
                {
                    Id = Guid.NewGuid(),
                    ClientGroupId = clientGroupId,
                    IsDisabled = isDisabled,
                }
            );
            Clients.Addresses.Add((clientIdentifier, InternetProtocolVersionEnum.IPv4, client.Id));
            ClientAddresses.Items.Add(
                new DnsClientAddressEntity
                {
                    Id = Guid.NewGuid(),
                    ClientId = client.Id,
                    ClientIdentifier = clientIdentifier,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                }
            );
            Changed();
            return client;
        }

        public void AssignListToClient(Guid clientId, Guid listId)
        {
            ClientListAssignments.Items.Add(
                new DnsClientListAssignmentEntity
                {
                    Id = Guid.NewGuid(),
                    ClientId = clientId,
                    ListId = listId,
                }
            );
            Changed();
        }

        private static T AddEntity<T>(List<T> collection, T entity)
        {
            collection.Add(entity);
            return entity;
        }
    }

    [Fact]
    public async Task EvaluateDomainAsync_DeniesADomainOnAnAssignedBlockList()
    {
        Fixture fixture = new();
        DnsClientGroupEntity defaultGroup = fixture.AddGroup("default", isDefault: true);
        DnsFilterListEntity blockList = fixture.AddList(
            "ads",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "ads.example.com"
        );
        fixture.AssignListToGroup(defaultGroup.Id, blockList.Id);

        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision decision =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse("ads.example.com"),
                IPAddress.Parse("10.0.0.5")
            );

        Assert.True(decision.IsDenied);
    }

    [Fact]
    public async Task EvaluateDomainAsync_DomainListEntryAlsoCoversSubdomains()
    {
        Fixture fixture = new();
        DnsClientGroupEntity defaultGroup = fixture.AddGroup("default", isDefault: true);
        DnsFilterListEntity blockList = fixture.AddList(
            "ads",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "example.com"
        );
        fixture.AssignListToGroup(defaultGroup.Id, blockList.Id);

        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision decision =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse("www.example.com"),
                IPAddress.Parse("10.0.0.5")
            );

        Assert.True(decision.IsDenied);
    }

    [Fact]
    public async Task EvaluateDomainAsync_AllowsWhenNoListMatches()
    {
        Fixture fixture = new();
        fixture.AddGroup("default", isDefault: true);

        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision decision =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse("safe.example.com"),
                IPAddress.Parse("10.0.0.5")
            );

        Assert.False(decision.IsDenied);
    }

    [Fact]
    public async Task EvaluateDomainAsync_ClientInExplicitGroupUsesThatGroupsListsNotDefaults()
    {
        Fixture fixture = new();
        DnsClientGroupEntity defaultGroup = fixture.AddGroup("default", isDefault: true);
        DnsFilterListEntity defaultBlockList = fixture.AddList(
            "default-block",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "blocked-in-default.example.com"
        );
        fixture.AssignListToGroup(defaultGroup.Id, defaultBlockList.Id);

        DnsClientGroupEntity kidsGroup = fixture.AddGroup("kids", isDefault: false);
        fixture.AddNetwork(kidsGroup.Id, "192.168.1.0/24");

        // The "kids" group has no lists, but since it matched, the default group's block list
        // must not apply.
        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision decision =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse("blocked-in-default.example.com"),
                IPAddress.Parse("192.168.1.50")
            );

        Assert.False(decision.IsDenied);
    }

    [Fact]
    public async Task EvaluateDomainAsync_AllowWinsByDefaultWhenBothListsMatch()
    {
        Fixture fixture = new();
        DnsClientGroupEntity defaultGroup = fixture.AddGroup("default", isDefault: true);
        DnsFilterListEntity blockList = fixture.AddList(
            "block",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "shared.example.com"
        );
        DnsFilterListEntity allowList = fixture.AddList(
            "allow",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Allow,
            "shared.example.com"
        );
        fixture.AssignListToGroup(defaultGroup.Id, blockList.Id);
        fixture.AssignListToGroup(defaultGroup.Id, allowList.Id);

        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision decision =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse("shared.example.com"),
                IPAddress.Parse("10.0.0.5")
            );

        Assert.False(decision.IsDenied);
    }

    [Fact]
    public async Task EvaluateDomainAsync_BlockWinsWhenConfiguredAndBothListsMatch()
    {
        Fixture fixture = new();
        fixture.Options.Precedence = DnsFilterListPrecedenceEnum.BlockWins;
        DnsClientGroupEntity defaultGroup = fixture.AddGroup("default", isDefault: true);
        DnsFilterListEntity blockList = fixture.AddList(
            "block",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "shared.example.com"
        );
        DnsFilterListEntity allowList = fixture.AddList(
            "allow",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Allow,
            "shared.example.com"
        );
        fixture.AssignListToGroup(defaultGroup.Id, blockList.Id);
        fixture.AssignListToGroup(defaultGroup.Id, allowList.Id);

        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision decision =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse("shared.example.com"),
                IPAddress.Parse("10.0.0.5")
            );

        Assert.True(decision.IsDenied);
    }

    [Fact]
    public async Task EvaluateClientAsync_DeniesAClientSourceOnAnAssignedBlockList()
    {
        Fixture fixture = new();
        DnsClientGroupEntity defaultGroup = fixture.AddGroup("default", isDefault: true);
        DnsFilterListEntity blockList = fixture.AddList(
            "banned-clients",
            DnsFilterListKindEnum.ClientSource,
            DnsFilterListActionEnum.Block,
            "203.0.113.0/24"
        );
        fixture.AssignListToGroup(defaultGroup.Id, blockList.Id);

        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision decision =
            await fixture.Engine.EvaluateClientAsync(IPAddress.Parse("203.0.113.7"));

        Assert.True(decision.IsDenied);
    }

    [Fact]
    public async Task EvaluateResponseAsync_DeniesAResponseContainingABlockedIp()
    {
        Fixture fixture = new();
        DnsClientGroupEntity defaultGroup = fixture.AddGroup("default", isDefault: true);
        DnsFilterListEntity blockList = fixture.AddList(
            "malware-ips",
            DnsFilterListKindEnum.ResponseIp,
            DnsFilterListActionEnum.Block,
            "198.51.100.23"
        );
        fixture.AssignListToGroup(defaultGroup.Id, blockList.Id);

        DnsMessage response = new()
        {
            Answers =
            {
                new DnsResourceRecord(
                    DnsName.Parse("evil.example.com"),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet,
                    60,
                    IPAddress.Parse("198.51.100.23").GetAddressBytes()
                ),
            },
        };

        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision decision =
            await fixture.Engine.EvaluateResponseAsync(response, IPAddress.Parse("10.0.0.5"));

        Assert.True(decision.IsDenied);
    }

    [Fact]
    public void BuildDeniedResponse_DefaultsToNxDomain()
    {
        Fixture fixture = new();
        DnsMessage request = new()
        {
            Id = 42,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("blocked.example.com"),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };

        DnsMessage response = fixture.Engine.BuildDeniedResponse(request);

        Assert.Equal(DnsResponseCodeEnum.NxDomain, response.ResponseCode);
        Assert.Empty(response.Answers);
    }

    [Fact]
    public void BuildDeniedResponse_ReturnsASinkholeRecordWhenConfigured()
    {
        Fixture fixture = new();
        fixture.Options.BlockResponseMode = DnsFilterBlockResponseModeEnum.SinkholeAddress;
        DnsMessage request = new()
        {
            Id = 42,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("blocked.example.com"),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };

        DnsMessage response = fixture.Engine.BuildDeniedResponse(request);

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Single(response.Answers);
        Assert.Equal(IPAddress.Parse("0.0.0.0"), new IPAddress(response.Answers[0].Data));
    }

    [Theory]
    [InlineData("ads.*.example.com", "ads.tracker.example.com", true)]
    [InlineData("ads.*.example.com", "ads.example.com", false)]
    [InlineData("ads.*.example.com", "safe.tracker.example.com", false)]
    [InlineData("ad?.example.com", "ads.example.com", true)]
    [InlineData("ad?.example.com", "adserver.example.com", false)]
    public async Task EvaluateDomainAsync_WildcardEntryMatchesGlobSyntax(
        string pattern,
        string queriedName,
        bool shouldBeDenied
    )
    {
        Fixture fixture = new();
        DnsClientGroupEntity defaultGroup = fixture.AddGroup("default", isDefault: true);
        DnsFilterListEntity blockList = fixture.AddListWithKindedEntry(
            "wildcard-block",
            DnsFilterListActionEnum.Block,
            DnsFilterEntryKindEnum.Wildcard,
            pattern
        );
        fixture.AssignListToGroup(defaultGroup.Id, blockList.Id);

        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision decision =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse(queriedName),
                IPAddress.Parse("10.0.0.5")
            );

        Assert.Equal(shouldBeDenied, decision.IsDenied);
    }

    [Theory]
    [InlineData(@"^ads?\.example\.com$", "ad.example.com", true)]
    [InlineData(@"^ads?\.example\.com$", "ads.example.com", true)]
    [InlineData(@"^ads?\.example\.com$", "www.example.com", false)]
    [InlineData(@"tracker", "www.tracker.example.com", true)]
    [InlineData(@"tracker", "safe.example.com", false)]
    public async Task EvaluateDomainAsync_RegexEntryMatchesDotNetRegexSyntax(
        string pattern,
        string queriedName,
        bool shouldBeDenied
    )
    {
        Fixture fixture = new();
        DnsClientGroupEntity defaultGroup = fixture.AddGroup("default", isDefault: true);
        DnsFilterListEntity blockList = fixture.AddListWithKindedEntry(
            "regex-block",
            DnsFilterListActionEnum.Block,
            DnsFilterEntryKindEnum.Regex,
            pattern
        );
        fixture.AssignListToGroup(defaultGroup.Id, blockList.Id);

        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision decision =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse(queriedName),
                IPAddress.Parse("10.0.0.5")
            );

        Assert.Equal(shouldBeDenied, decision.IsDenied);
    }

    [Fact]
    public async Task EvaluateDomainAsync_MalformedRegexEntryIsTreatedAsANonMatchNotAThrow()
    {
        Fixture fixture = new();
        DnsClientGroupEntity defaultGroup = fixture.AddGroup("default", isDefault: true);
        // "(" is not a valid regex. FilterListEntriesController rejects it on write, but a row
        // written another way must still not make the engine throw.
        DnsFilterListEntity blockList = fixture.AddListWithKindedEntry(
            "malformed-regex-block",
            DnsFilterListActionEnum.Block,
            DnsFilterEntryKindEnum.Regex,
            "("
        );
        fixture.AssignListToGroup(defaultGroup.Id, blockList.Id);

        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision decision =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse("anything.example.com"),
                IPAddress.Parse("10.0.0.5")
            );

        Assert.False(decision.IsDenied);
    }

    [Fact]
    public async Task EvaluateDomainAsync_CatastrophicBacktrackingRegexTimesOutRatherThanHanging()
    {
        Fixture fixture = new();
        DnsClientGroupEntity defaultGroup = fixture.AddGroup("default", isDefault: true);
        // (a+)+$ against a run of "a" with a non-matching tail is the textbook
        // catastrophic-backtracking regex: its run time grows exponentially with the input.
        DnsFilterListEntity blockList = fixture.AddListWithKindedEntry(
            "pathological-regex-block",
            DnsFilterListActionEnum.Block,
            DnsFilterEntryKindEnum.Regex,
            "^(a+)+$"
        );
        fixture.AssignListToGroup(defaultGroup.Id, blockList.Id);

        string pathologicalInput = new string('a', 40) + "b";

        Stopwatch stopwatch = Stopwatch.StartNew();
        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision decision =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse(pathologicalInput + ".example.com"),
                IPAddress.Parse("10.0.0.5")
            );
        stopwatch.Stop();

        // The time limit, not only the result, is what shows the match timeout is applied.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Expected the timeout-guarded match to give up quickly; it took {stopwatch.Elapsed}."
        );
        Assert.False(decision.IsDenied);
    }

    /// <summary>
    ///     This test checks that an engine built while <see cref="DnsFilterOptions.Enable" /> is
    ///     off starts denying, without being rebuilt, once the flag turns on, because it reads
    ///     <c>CurrentValue</c> on every call.
    /// </summary>
    [Fact]
    public async Task EvaluateDomainAsync_WhenEnableFlipsLiveOnTheSharedOptionsObject_TheSameEngineInstanceStartsDenying()
    {
        Fixture fixture = new();
        fixture.Options.Enable = false;

        DnsClientGroupEntity defaultGroup = fixture.AddGroup("default", isDefault: true);
        DnsFilterListEntity blockList = fixture.AddList(
            "ads",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "ads.example.com"
        );
        fixture.AssignListToGroup(defaultGroup.Id, blockList.Id);

        // A matching entry exists, so an allow here means "disabled", not "no match".
        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision before =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse("ads.example.com"),
                IPAddress.Parse("10.0.0.5")
            );
        Assert.False(before.IsDenied);

        fixture.Options.Enable = true;

        // The same instance now denies.
        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision after =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse("ads.example.com"),
                IPAddress.Parse("10.0.0.5")
            );
        Assert.True(after.IsDenied);
    }

    /// <summary>
    ///     This test checks the client-registry way into a group: a query whose source address
    ///     resolves to a client with a <see cref="DnsClientEntity.ClientGroupId" /> gets that
    ///     group's lists even though no network row matches it.
    /// </summary>
    [Fact]
    public async Task EvaluateDomainAsync_ClientResolvedToAGroupWithNoMatchingNetwork_UsesThatGroupsLists()
    {
        IPAddress clientAddress = IPAddress.Parse("10.0.0.42");
        FakeClientIdentityResolver resolver = new(
            clientAddress,
            new ClientIdentityResolution("aa:bb:cc:dd:ee:ff", InternetProtocolVersionEnum.IPv4)
        );
        Fixture fixture = new(resolver);

        fixture.AddGroup("default", isDefault: true);
        DnsClientGroupEntity kidsGroup = fixture.AddGroup("kids", isDefault: false);
        // No network row for "kids", so only the client registry can match it.
        DnsFilterListEntity kidsBlockList = fixture.AddList(
            "kids-block",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "blocked-for-kids.example.com"
        );
        fixture.AssignListToGroup(kidsGroup.Id, kidsBlockList.Id);

        fixture.AddClient("aa:bb:cc:dd:ee:ff", clientGroupId: kidsGroup.Id);

        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision decision =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse("blocked-for-kids.example.com"),
                clientAddress
            );

        Assert.True(decision.IsDenied);
    }

    /// <summary>
    ///     This test checks that a resolved client's direct <see cref="DnsClientListAssignmentEntity" />
    ///     rows are added to its group's lists: the group's block list and the client's own block
    ///     list each deny a query that only they match.
    /// </summary>
    [Fact]
    public async Task EvaluateDomainAsync_ClientsOwnDirectListAssignmentIsUnionedWithItsGroupsLists()
    {
        IPAddress clientAddress = IPAddress.Parse("10.0.0.43");
        FakeClientIdentityResolver resolver = new(
            clientAddress,
            new ClientIdentityResolution("11:22:33:44:55:66", InternetProtocolVersionEnum.IPv4)
        );
        Fixture fixture = new(resolver);

        DnsClientGroupEntity defaultGroup = fixture.AddGroup("default", isDefault: true);
        DnsFilterListEntity groupBlockList = fixture.AddList(
            "group-block",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "blocked-by-group.example.com"
        );
        fixture.AssignListToGroup(defaultGroup.Id, groupBlockList.Id);

        DnsFilterListEntity clientBlockList = fixture.AddList(
            "client-block",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "blocked-for-this-client-only.example.com"
        );
        DnsClientEntity client = fixture.AddClient("11:22:33:44:55:66");
        fixture.AssignListToClient(client.Id, clientBlockList.Id);

        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision viaGroup =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse("blocked-by-group.example.com"),
                clientAddress
            );
        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision viaClient =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse("blocked-for-this-client-only.example.com"),
                clientAddress
            );

        Assert.True(viaGroup.IsDenied);
        Assert.True(viaClient.IsDenied);
    }

    /// <summary>
    ///     This test checks that a disabled client contributes neither its group nor its direct
    ///     list assignments, either of which would otherwise deny this query.
    /// </summary>
    [Fact]
    public async Task EvaluateDomainAsync_DisabledClientContributesNothing()
    {
        IPAddress clientAddress = IPAddress.Parse("10.0.0.44");
        FakeClientIdentityResolver resolver = new(
            clientAddress,
            new ClientIdentityResolution("de:ad:be:ef:00:01", InternetProtocolVersionEnum.IPv4)
        );
        Fixture fixture = new(resolver);

        fixture.AddGroup("default", isDefault: true);
        DnsClientGroupEntity kidsGroup = fixture.AddGroup("kids", isDefault: false);
        DnsFilterListEntity kidsBlockList = fixture.AddList(
            "kids-block",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "blocked-for-kids.example.com"
        );
        fixture.AssignListToGroup(kidsGroup.Id, kidsBlockList.Id);

        DnsFilterListEntity clientBlockList = fixture.AddList(
            "client-block",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "blocked-directly.example.com"
        );
        DnsClientEntity disabledClient = fixture.AddClient(
            "de:ad:be:ef:00:01",
            clientGroupId: kidsGroup.Id,
            isDisabled: true
        );
        fixture.AssignListToClient(disabledClient.Id, clientBlockList.Id);

        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision viaGroup =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse("blocked-for-kids.example.com"),
                clientAddress
            );
        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision viaClient =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse("blocked-directly.example.com"),
                clientAddress
            );

        Assert.False(viaGroup.IsDenied);
        Assert.False(viaClient.IsDenied);
    }

    /// <summary>
    ///     This test checks that a client in a <em>disabled</em> group contributes no group match,
    ///     but its direct list assignment still applies (unlike a disabled client, see
    ///     <see cref="EvaluateDomainAsync_DisabledClientContributesNothing" />).
    /// </summary>
    [Fact]
    public async Task EvaluateDomainAsync_ClientResolvedToADisabledGroupContributesNoGroupMatchButStillContributesItsOwnDirectAssignment()
    {
        IPAddress clientAddress = IPAddress.Parse("10.0.0.45");
        FakeClientIdentityResolver resolver = new(
            clientAddress,
            new ClientIdentityResolution("ca:fe:ba:be:00:02", InternetProtocolVersionEnum.IPv4)
        );
        Fixture fixture = new(resolver);

        fixture.AddGroup("default", isDefault: true);
        DnsClientGroupEntity disabledGroup = fixture.AddGroup("disabled-group", isDefault: false);
        disabledGroup.IsDisabled = true;
        fixture.Changed();
        DnsFilterListEntity groupBlockList = fixture.AddList(
            "group-block",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "blocked-by-disabled-group.example.com"
        );
        fixture.AssignListToGroup(disabledGroup.Id, groupBlockList.Id);

        DnsFilterListEntity clientBlockList = fixture.AddList(
            "client-block",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "blocked-directly.example.com"
        );
        DnsClientEntity client = fixture.AddClient(
            "ca:fe:ba:be:00:02",
            clientGroupId: disabledGroup.Id
        );
        fixture.AssignListToClient(client.Id, clientBlockList.Id);

        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision viaGroup =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse("blocked-by-disabled-group.example.com"),
                clientAddress
            );
        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision viaClient =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse("blocked-directly.example.com"),
                clientAddress
            );

        Assert.False(viaGroup.IsDenied);
        Assert.True(viaClient.IsDenied);
    }

    /// <summary>
    ///     This test checks that without an <see cref="IClientIdentityResolver" />, a registered
    ///     <see cref="DnsClientEntity" /> is never consulted: the client row would move the query
    ///     into a group whose allow list lets it through, but the default group's block list
    ///     decides.
    /// </summary>
    [Fact]
    public async Task EvaluateDomainAsync_WithNoClientIdentityResolverWiredUp_ClientDerivedRegistryRowsAreNeverConsidered()
    {
        Fixture fixture = new(identityResolver: null);
        IPAddress clientAddress = IPAddress.Parse("10.0.0.46");

        DnsClientGroupEntity defaultGroup = fixture.AddGroup("default", isDefault: true);
        DnsClientGroupEntity kidsGroup = fixture.AddGroup("kids", isDefault: false);
        DnsFilterListEntity kidsAllowList = fixture.AddList(
            "kids-allow",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Allow,
            "shared.example.com"
        );
        fixture.AssignListToGroup(kidsGroup.Id, kidsAllowList.Id);
        DnsFilterListEntity defaultBlockList = fixture.AddList(
            "default-block",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "shared.example.com"
        );
        fixture.AssignListToGroup(defaultGroup.Id, defaultBlockList.Id);

        // With a resolver, this row would put the address in "kids" and its allow list would
        // win. Without one, the row must have no effect.
        fixture.AddClient("00:11:22:33:44:55", clientGroupId: kidsGroup.Id);

        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision decision =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse("shared.example.com"),
                clientAddress
            );

        Assert.True(decision.IsDenied);
    }

    /// <summary>
    ///     This test checks that when a resolver resolves the address but neither
    ///     <c>IDnsClientRepository</c> nor <c>IDnsClientListAssignmentRepository</c> is
    ///     registered, the engine does not throw and falls back to network and default-group
    ///     matching.
    /// </summary>
    [Fact]
    public async Task EvaluateDomainAsync_WithClientRepositoriesAbsentFromDi_FallsBackToOrdinaryResolutionWithoutThrowing()
    {
        IPAddress clientAddress = IPAddress.Parse("10.0.0.47");
        FakeClientIdentityResolver resolver = new(
            clientAddress,
            new ClientIdentityResolution("ab:cd:ef:00:11:22", InternetProtocolVersionEnum.IPv4)
        );
        Fixture fixture = new(resolver, registerClientRepositories: false);

        DnsClientGroupEntity defaultGroup = fixture.AddGroup("default", isDefault: true);
        DnsFilterListEntity blockList = fixture.AddList(
            "ads",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "ads.example.com"
        );
        fixture.AssignListToGroup(defaultGroup.Id, blockList.Id);

        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision decision =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse("ads.example.com"),
                clientAddress
            );

        Assert.True(decision.IsDenied);
    }
}
