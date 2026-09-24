using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Filtering.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering.Tests;

public partial class DnsFilterEngineTests
{
    private sealed class Fixture
    {
        public readonly InMemoryDnsFilterListRepository Lists = new();
        public readonly InMemoryDnsFilterListEntryRepository Entries = new();
        public readonly InMemoryDnsClientGroupRepository Groups = new();
        public readonly InMemoryDnsClientGroupNetworkRepository Networks = new();
        public readonly InMemoryDnsClientGroupListAssignmentRepository Assignments = new();
        public readonly InMemoryDnsClientRepository Clients = new();
        public readonly InMemoryDnsClientListAssignmentRepository ClientListAssignments = new();
        public readonly DnsFilterOptions Options = new() { Enable = true };
        public readonly IServiceScopeFactory ScopeFactory;
        public readonly DnsFilterEngine Engine;

        /// <param name="identityResolver">
        ///     The optional client identity resolver the engine under test is constructed with --
        ///     <see langword="null" /> (the default) reproduces this engine's exact pre-client-
        ///     identity-integration construction, the regression baseline every pre-existing test
        ///     in this file already exercises.
        /// </param>
        /// <param name="registerClientRepositories">
        ///     Whether <see cref="Clients" />/<see cref="ClientListAssignments" /> are registered
        ///     in the engine's own DI container at all -- <see langword="false" /> proves the
        ///     engine never even attempts to resolve either repository type when it has no
        ///     identity resolver to pair them with.
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
                services.AddSingleton<Ninlil.Dns.Data.Abstractions.Repository.IDnsClientListAssignmentRepository>(
                    ClientListAssignments
                );
            }

            ScopeFactory = services
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>();
            Engine = new DnsFilterEngine(
                ScopeFactory,
                Options2(),
                NullLogger<DnsFilterEngine>.Instance,
                identityResolver
            );
        }

        private StaticOptionsMonitor<DnsFilterOptions> Options2() => new(Options);

        public DnsClientGroupEntity AddGroup(string name, bool isDefault) =>
            AddEntity(
                Groups.Items,
                new DnsClientGroupEntity
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    IsDefault = isDefault,
                }
            );

        public void AddNetwork(Guid groupId, string cidr) =>
            Networks.Items.Add(
                new DnsClientGroupNetworkEntity
                {
                    Id = Guid.NewGuid(),
                    ClientGroupId = groupId,
                    Cidr = cidr,
                }
            );

        public void AddClientIdNetwork(Guid groupId, string clientId) =>
            Networks.Items.Add(
                new DnsClientGroupNetworkEntity
                {
                    Id = Guid.NewGuid(),
                    ClientGroupId = groupId,
                    ClientId = clientId,
                }
            );

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

            return list;
        }

        public void AssignListToGroup(Guid groupId, Guid listId) =>
            Assignments.Items.Add(
                new DnsClientGroupListAssignmentEntity
                {
                    Id = Guid.NewGuid(),
                    ClientGroupId = groupId,
                    ListId = listId,
                }
            );

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
            return client;
        }

        public void AssignListToClient(Guid clientId, Guid listId) =>
            ClientListAssignments.Items.Add(
                new DnsClientListAssignmentEntity
                {
                    Id = Guid.NewGuid(),
                    ClientId = clientId,
                    ListId = listId,
                }
            );

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

        // The "kids" group has no lists of its own assigned -- since it matched explicitly, the
        // default group's own block list must NOT also apply.
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
        // "(" is not a well-formed regular expression -- this can only ever reach the engine via
        // a row written by something other than FilterListEntriesController's own
        // compile-before-persist validation (a direct repository call, an import tool), but the
        // engine itself must still never throw for it.
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
        // (a+)+$ against a long run of "a" characters with no trailing match is the textbook
        // catastrophic-backtracking regex -- without a real matchTimeout this would hang for an
        // effectively unbounded amount of time (exponential in the input length) rather than
        // ever completing.
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

        // A real regression here would hang for seconds-to-minutes (the input above is
        // deliberately short enough that an unguarded .NET regex engine still takes many seconds
        // to exhaust its own backtracking, without hanging this test suite itself forever) --
        // asserting a generous, but real, wall-clock ceiling well under that is what makes this a
        // genuine regression test for the timeout wiring, not merely a coincidence of the boolean
        // result alone.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Expected the timeout-guarded match to give up quickly; it took {stopwatch.Elapsed}."
        );
        Assert.False(decision.IsDenied);
    }

    /// <summary>
    ///     This test proves a structural invariant: <see cref="DnsFilterEngine" /> is
    ///     constructed and consulted regardless of <see cref="DnsFilterOptions.Enable" />'s own
    ///     value at construction time, with its own live check reading <c>IOptionsMonitor</c>'s
    ///     <c>CurrentValue</c> fresh on every call -- so the *same, already-constructed* engine
    ///     instance genuinely starts denying once <see cref="DnsFilterOptions.Enable" /> flips to
    ///     <see langword="true" /> on the shared options object it was built against, with no
    ///     restart and no new instance, mirroring
    ///     <c>RecursiveOptionsHotReloadTests</c>'s own "same instance, before/after" proof shape
    ///     for <c>RecursiveServerOptions</c>.
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

        // Before: a real, matching block-list entry exists, but the engine is disabled, so the
        // early-exit gate wins -- this is the "structural" half of the proof, distinguishing "no
        // match" from "declined because disabled."
        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision before =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse("ads.example.com"),
                IPAddress.Parse("10.0.0.5")
            );
        Assert.False(before.IsDenied);

        fixture.Options.Enable = true;

        // After: the identical engine instance, with no reconstruction, now genuinely evaluates
        // the same matching entry and denies it.
        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision after =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse("ads.example.com"),
                IPAddress.Parse("10.0.0.5")
            );
        Assert.True(after.IsDenied);
    }

    /// <summary>
    ///     This test proves the new, third way into a group (15c): a query whose source address
    ///     resolves to a known client with an explicit <see cref="DnsClientEntity.ClientGroupId" />
    ///     gets that group's own filter lists applied even though no CIDR/DoH-ClientID network
    ///     row would ever have matched it on its own.
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
        // Deliberately no DnsClientGroupNetworkEntity row for "kids" at all -- the only way this
        // group can ever match this query is via the client-derived path under test.
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
    ///     This test proves a resolved client's own direct <see cref="DnsClientListAssignmentEntity" />
    ///     rows are unioned with whatever its matched group already contributes -- both the
    ///     group's own block list and the client's own separately assigned block list must each
    ///     independently deny a query matching only one of the two.
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
    ///     This test proves a disabled client contributes neither a client-derived group match
    ///     nor its own direct list assignments -- both would otherwise deny this query.
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
    ///     This test proves a client resolved to a <em>disabled</em> group contributes no group
    ///     match -- but, unlike a disabled client (see
    ///     <see cref="EvaluateDomainAsync_DisabledClientContributesNothing" />), its own direct
    ///     list assignment is unaffected, since only the group itself is disabled here.
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
    ///     This is the explicit regression guarantee for 15c: with no
    ///     <see cref="IClientIdentityResolver" /> wired up at all (the exact construction every
    ///     pre-existing test in this file already uses), a matching <see cref="DnsClientEntity" />
    ///     row that -- if the resolver existed -- would flip this query to denied is never even
    ///     considered. CIDR-based group resolution alone continues to decide this query, byte for
    ///     byte identical to this engine's behavior before this sub-phase.
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

        // If a real IClientIdentityResolver/IDnsClientRepository pair were consulted, this row
        // would resolve this exact address to the "kids" group -- flipping the decision to
        // allowed via that group's own allow list. With no resolver wired up at all, this row
        // must be completely inert.
        fixture.AddClient("00:11:22:33:44:55", clientGroupId: kidsGroup.Id);

        Ninlil.Dns.Server.Core.Filtering.DnsFilterDecision decision =
            await fixture.Engine.EvaluateDomainAsync(
                DnsName.Parse("shared.example.com"),
                clientAddress
            );

        Assert.True(decision.IsDenied);
    }

    /// <summary>
    ///     This test covers the other absent-infrastructure shape: a real resolver is present and
    ///     genuinely resolves this query's own address, but neither
    ///     <c>IDnsClientRepository</c> nor <c>IDnsClientListAssignmentRepository</c>
    ///     is registered in this engine's own DI container at all (the shape a deployment without
    ///     the known-client registry's own storage wired up would have). The engine must not
    ///     throw, and must fall back to ordinary CIDR/default-group resolution exactly as if the
    ///     resolver did not exist either.
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
