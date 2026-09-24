using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Rewrites.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Rewrites.Tests;

public class DnsRewriteEngineTests
{
    private sealed class Fixture
    {
        public readonly InMemoryDnsRewriteRepository Rewrites = new();
        public readonly InMemoryDnsClientRepository Clients = new();
        public readonly DnsRewriteOptions Options = new() { Enable = true, TimeToLiveSeconds = 60 };
        public readonly DnsRewriteEngine Engine;

        /// <param name="clientIdentityResolver">
        ///     The optional resolver to construct the engine with -- <see langword="null" /> (the
        ///     default) reproduces this engine's own pre-client-scoping construction exactly, the
        ///     shape every pre-existing test in this file still uses.
        /// </param>
        /// <param name="registerClientRepository">
        ///     Whether <see cref="Clients" /> is registered in the same service provider the
        ///     engine's own scope factory resolves against -- <see langword="false" /> reproduces
        ///     the "resolver present, but no client repository wired up" absent-infrastructure case
        ///     a dedicated regression test below covers.
        /// </param>
        public Fixture(
            IClientIdentityResolver clientIdentityResolver = null,
            bool registerClientRepository = true
        )
        {
            ServiceCollection services = new();
            services.AddSingleton<IDnsRewriteRepository>(Rewrites);

            if (registerClientRepository)
                services.AddSingleton<IDnsClientRepository>(Clients);

            IServiceScopeFactory scopeFactory = services
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>();

            Engine = new DnsRewriteEngine(
                scopeFactory,
                new StaticOptionsMonitor<DnsRewriteOptions>(Options),
                NullLogger<DnsRewriteEngine>.Instance,
                clientIdentityResolver
            );
        }

        public DnsRewriteEntity Add(string domain, string target, bool isDisabled = false) =>
            Add(domain, target, DateTimeOffset.UtcNow, isDisabled);

        public DnsRewriteEntity Add(
            string domain,
            string target,
            DateTimeOffset createdAtUtc,
            bool isDisabled = false
        )
        {
            DnsRewriteEntity entity = new()
            {
                Id = Guid.NewGuid(),
                Domain = domain,
                Target = target,
                IsDisabled = isDisabled,
                CreatedAtUtc = createdAtUtc,
            };
            Rewrites.Items.Add(entity);
            return entity;
        }

        /// <summary>
        ///     This method adds a rewrite scoped directly to a single client.
        /// </summary>
        public DnsRewriteEntity AddClientScoped(string domain, string target, Guid clientId)
        {
            DnsRewriteEntity entity = Add(domain, target);
            entity.ClientId = clientId;
            return entity;
        }

        /// <summary>
        ///     This method adds a rewrite scoped to a single client group.
        /// </summary>
        public DnsRewriteEntity AddGroupScoped(string domain, string target, Guid clientGroupId)
        {
            DnsRewriteEntity entity = Add(domain, target);
            entity.ClientGroupId = clientGroupId;
            return entity;
        }

        /// <summary>
        ///     This method registers a known, non-disabled client row for a given identifier/
        ///     protocol version pair, optionally naming its own client group.
        /// </summary>
        public DnsClientEntity AddClient(
            string clientIdentifier,
            Guid? clientGroupId = null,
            bool isDisabled = false
        )
        {
            DnsClientEntity client = new()
            {
                Id = Guid.NewGuid(),
                ClientGroupId = clientGroupId,
                IsDisabled = isDisabled,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            Clients.Items.Add(client);
            Clients.Addresses.Add((clientIdentifier, InternetProtocolVersionEnum.IPv4, client.Id));
            return client;
        }
    }

    private static DnsMessage BuildRequest(string name, DnsResourceRecordTypeEnum type) =>
        new()
        {
            Id = 1,
            RecursionDesired = true,
            Questions = [new DnsQuestion(DnsName.Parse(name), type, DnsClassEnum.Internet)],
        };

    [Fact]
    public async Task ExactADomainRewrite_ForAQuery_AnswersWithTheConfiguredAddress()
    {
        Fixture fixture = new();
        fixture.Add("host.example.com", "203.0.113.5");

        DnsMessage response = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("host.example.com", DnsResourceRecordTypeEnum.A)
        );

        Assert.NotNull(response);
        Assert.Single(response.Answers);
        Assert.Equal(DnsResourceRecordTypeEnum.A, response.Answers[0].Type);
        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Equal("203.0.113.5", new System.Net.IPAddress(response.Answers[0].Data).ToString());
    }

    [Fact]
    public async Task ExactAaaaDomainRewrite_ForAaaaQuery_AnswersWithTheConfiguredAddress()
    {
        Fixture fixture = new();
        fixture.Add("host.example.com", "2001:db8::1");

        DnsMessage response = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("host.example.com", DnsResourceRecordTypeEnum.Aaaa)
        );

        Assert.NotNull(response);
        Assert.Single(response.Answers);
        Assert.Equal(DnsResourceRecordTypeEnum.Aaaa, response.Answers[0].Type);
    }

    [Fact]
    public async Task AOnlyRewrite_ForAaaaQuery_DoesNotMatch()
    {
        Fixture fixture = new();
        fixture.Add("host.example.com", "203.0.113.5");

        DnsMessage response = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("host.example.com", DnsResourceRecordTypeEnum.Aaaa)
        );

        Assert.Null(response);
    }

    [Fact]
    public async Task DomainTargetRewrite_ForAQuery_AnswersWithACnameRecord()
    {
        Fixture fixture = new();
        fixture.Add("alias.example.com", "canonical.example.net");

        DnsMessage response = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("alias.example.com", DnsResourceRecordTypeEnum.A)
        );

        Assert.NotNull(response);
        Assert.Single(response.Answers);
        Assert.Equal(DnsResourceRecordTypeEnum.Cname, response.Answers[0].Type);
    }

    [Fact]
    public async Task DomainTargetRewrite_ForCnameQuery_AlsoAnswers()
    {
        Fixture fixture = new();
        fixture.Add("alias.example.com", "canonical.example.net");

        DnsMessage response = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("alias.example.com", DnsResourceRecordTypeEnum.Cname)
        );

        Assert.NotNull(response);
        Assert.Equal(DnsResourceRecordTypeEnum.Cname, response.Answers[0].Type);
    }

    [Fact]
    public async Task ExactDomainRewrite_DoesNotAlsoMatchASubdomain()
    {
        Fixture fixture = new();
        fixture.Add("example.com", "203.0.113.5");

        DnsMessage response = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("www.example.com", DnsResourceRecordTypeEnum.A)
        );

        Assert.Null(response);
    }

    [Fact]
    public async Task WildcardDomainRewrite_MatchesASubdomain_ButNotTheApexItself()
    {
        Fixture fixture = new();
        fixture.Add("*.example.com", "203.0.113.9");

        DnsMessage subdomainResponse = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("ads.example.com", DnsResourceRecordTypeEnum.A)
        );
        DnsMessage apexResponse = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("example.com", DnsResourceRecordTypeEnum.A)
        );

        Assert.NotNull(subdomainResponse);
        Assert.Equal(
            "203.0.113.9",
            new System.Net.IPAddress(subdomainResponse.Answers[0].Data).ToString()
        );
        Assert.Null(apexResponse);
    }

    [Fact]
    public async Task WildcardDomainRewrite_MatchesAMultiLabelSubdomain()
    {
        Fixture fixture = new();
        fixture.Add("*.example.com", "203.0.113.9");

        DnsMessage response = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("deep.nested.example.com", DnsResourceRecordTypeEnum.A)
        );

        Assert.NotNull(response);
    }

    [Fact]
    public async Task ExactMatch_TakesPrecedenceOverAWildcardMatch()
    {
        Fixture fixture = new();
        fixture.Add("*.example.com", "203.0.113.9");
        fixture.Add("ads.example.com", "203.0.113.42");

        DnsMessage response = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("ads.example.com", DnsResourceRecordTypeEnum.A)
        );

        Assert.NotNull(response);
        Assert.Equal("203.0.113.42", new System.Net.IPAddress(response.Answers[0].Data).ToString());
    }

    [Fact]
    public async Task DisabledRewrite_IsNeverConsulted()
    {
        Fixture fixture = new();
        fixture.Add("host.example.com", "203.0.113.5", isDisabled: true);

        DnsMessage response = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("host.example.com", DnsResourceRecordTypeEnum.A)
        );

        Assert.Null(response);
    }

    [Fact]
    public async Task NoMatchingRewrite_ReturnsNull()
    {
        Fixture fixture = new();
        fixture.Add("host.example.com", "203.0.113.5");

        DnsMessage response = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("other.example.com", DnsResourceRecordTypeEnum.A)
        );

        Assert.Null(response);
    }

    [Fact]
    public async Task NonInterceptedQuestionType_FallsThroughUnansweredEvenWithAMatchingDomain()
    {
        Fixture fixture = new();
        fixture.Add("host.example.com", "203.0.113.5");

        DnsMessage response = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("host.example.com", DnsResourceRecordTypeEnum.Txt)
        );

        Assert.Null(response);
    }

    [Fact]
    public async Task EmptyRewriteSet_ReturnsNullWithoutError()
    {
        Fixture fixture = new();

        DnsMessage response = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("host.example.com", DnsResourceRecordTypeEnum.A)
        );

        Assert.Null(response);
    }

    [Fact]
    public async Task PathologicalWildcardMatch_TimesOutRatherThanHangingTheQuery()
    {
        Fixture fixture = new();
        // A wildcard glob with many '*' segments translates to a regex with many ".*" segments,
        // which can exhibit catastrophic backtracking against a long, non-matching input --
        // the same real regression shape DnsFilterEngineTests already covers for its own
        // timeout wiring, reproduced here independently since this engine compiles its own
        // regex rather than sharing DnsFilterEngine's.
        fixture.Add(
            new string('*', 1) + string.Concat(Enumerable.Repeat("a*", 30)) + "z",
            "203.0.113.9"
        );

        string nonMatchingName = string.Concat(Enumerable.Repeat("a", 40)) + ".example.com";

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        DnsMessage response = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest(nonMatchingName, DnsResourceRecordTypeEnum.A)
        );
        stopwatch.Stop();

        Assert.Null(response);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Expected the match to be timeout-guarded, but it took {stopwatch.Elapsed}."
        );
    }

    /// <summary>
    ///     This test proves a structural fix: <see cref="DnsRewriteEngine" /> is
    ///     now constructed and consulted regardless of <see cref="DnsRewriteOptions.Enable" />'s
    ///     own value at construction time, with its own live check reading
    ///     <c>IOptionsMonitor</c>'s <c>CurrentValue</c> fresh on every call -- so the *same,
    ///     already-constructed* engine instance genuinely starts answering once
    ///     <see cref="DnsRewriteOptions.Enable" /> flips to <see langword="true" /> on the shared
    ///     options object it was built against, with no restart and no new instance, mirroring
    ///     <c>RecursiveOptionsHotReloadTests</c>'s own "same instance, before/after" proof shape
    ///     for <c>RecursiveServerOptions</c>.
    /// </summary>
    [Fact]
    public async Task TryBuildResponseAsync_WhenEnableFlipsLiveOnTheSharedOptionsObject_TheSameEngineInstanceStartsAnswering()
    {
        Fixture fixture = new();
        fixture.Options.Enable = false;
        fixture.Add("host.example.com", "203.0.113.5");

        // Before: a real, matching rewrite exists, but the engine is disabled, so the early-exit
        // gate wins -- this is the "structural" half of the proof, distinguishing "no match" from
        // "declined because disabled."
        DnsMessage before = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("host.example.com", DnsResourceRecordTypeEnum.A)
        );
        Assert.Null(before);

        fixture.Options.Enable = true;

        // After: the identical engine instance, with no reconstruction, now genuinely matches and
        // synthesizes the same rewrite's own answer.
        DnsMessage after = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("host.example.com", DnsResourceRecordTypeEnum.A)
        );
        Assert.NotNull(after);
        Assert.Single(after.Answers);
        Assert.Equal("203.0.113.5", new System.Net.IPAddress(after.Answers[0].Data).ToString());
    }

    private static readonly IPAddress ClientOneAddress = IPAddress.Parse("192.0.2.10");
    private static readonly IPAddress ClientTwoAddress = IPAddress.Parse("192.0.2.20");
    private static readonly IPAddress UnknownAddress = IPAddress.Parse("192.0.2.99");

    /// <summary>
    ///     This test proves the core backward-compatibility property client scoping must never
    ///     violate: an ordinary, unscoped rewrite still applies to every query exactly as before,
    ///     even when a real <see cref="IClientIdentityResolver" /> is wired up and genuinely
    ///     resolves the querying address to a known client.
    /// </summary>
    [Fact]
    public async Task UnscopedRewrite_StillAppliesToEveryQuery_EvenWithClientIdentityInfrastructurePresent()
    {
        FakeClientIdentityResolver resolver = new FakeClientIdentityResolver().WithResolution(
            ClientOneAddress,
            new ClientIdentityResolution("aa:bb:cc:dd:ee:01", InternetProtocolVersionEnum.IPv4)
        );
        Fixture fixture = new(resolver);
        fixture.AddClient("aa:bb:cc:dd:ee:01");
        fixture.Add("host.example.com", "203.0.113.5");

        DnsMessage fromKnownClient = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("host.example.com", DnsResourceRecordTypeEnum.A),
            ClientOneAddress
        );
        DnsMessage fromUnknownAddress = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("host.example.com", DnsResourceRecordTypeEnum.A),
            UnknownAddress
        );

        Assert.NotNull(fromKnownClient);
        Assert.Equal("203.0.113.5", new IPAddress(fromKnownClient.Answers[0].Data).ToString());
        Assert.NotNull(fromUnknownAddress);
        Assert.Equal("203.0.113.5", new IPAddress(fromUnknownAddress.Answers[0].Data).ToString());
    }

    /// <summary>
    ///     This test proves the same backward-compatibility property holds with no client-identity
    ///     infrastructure wired up at all (the default <see cref="Fixture" /> shape every
    ///     pre-existing test in this file already uses) -- an explicit, dedicated regression rather
    ///     than only ever being implied by every other test never passing a <c>remoteAddress</c>.
    /// </summary>
    [Fact]
    public async Task UnscopedRewrite_StillApplies_WithNoClientIdentityResolverAtAll()
    {
        Fixture fixture = new();
        fixture.Add("host.example.com", "203.0.113.5");

        DnsMessage response = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("host.example.com", DnsResourceRecordTypeEnum.A),
            ClientOneAddress
        );

        Assert.NotNull(response);
        Assert.Equal("203.0.113.5", new IPAddress(response.Answers[0].Data).ToString());
    }

    /// <summary>
    ///     This test proves the same backward-compatibility property holds when a resolver is
    ///     wired up but the client repository itself is not registered in dependency injection --
    ///     the second absent-infrastructure shape <c>DnsFilterEngine</c>'s own equivalent tests
    ///     already cover.
    /// </summary>
    [Fact]
    public async Task UnscopedRewrite_StillApplies_WhenResolverPresentButClientRepositoryAbsent()
    {
        FakeClientIdentityResolver resolver = new FakeClientIdentityResolver().WithResolution(
            ClientOneAddress,
            new ClientIdentityResolution("aa:bb:cc:dd:ee:01", InternetProtocolVersionEnum.IPv4)
        );
        Fixture fixture = new(resolver, registerClientRepository: false);
        fixture.Add("host.example.com", "203.0.113.5");

        DnsMessage response = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("host.example.com", DnsResourceRecordTypeEnum.A),
            ClientOneAddress
        );

        Assert.NotNull(response);
        Assert.Equal("203.0.113.5", new IPAddress(response.Answers[0].Data).ToString());
    }

    /// <summary>
    ///     This test proves a client-scoped rewrite answers only its own client's queries -- an
    ///     unrelated, unresolved address gets no answer for the exact same question.
    /// </summary>
    [Fact]
    public async Task ClientScopedRewrite_AppliesOnlyToItsOwnClient()
    {
        FakeClientIdentityResolver resolver = new FakeClientIdentityResolver().WithResolution(
            ClientOneAddress,
            new ClientIdentityResolution("aa:bb:cc:dd:ee:01", InternetProtocolVersionEnum.IPv4)
        );
        Fixture fixture = new(resolver);
        DnsClientEntity client = fixture.AddClient("aa:bb:cc:dd:ee:01");
        fixture.AddClientScoped("kid-device.example.com", "restrict.example.net", client.Id);

        DnsMessage forItsClient = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("kid-device.example.com", DnsResourceRecordTypeEnum.A),
            ClientOneAddress
        );
        DnsMessage forUnknownAddress = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("kid-device.example.com", DnsResourceRecordTypeEnum.A),
            UnknownAddress
        );
        DnsMessage withNoAddressAtAll = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("kid-device.example.com", DnsResourceRecordTypeEnum.A)
        );

        Assert.NotNull(forItsClient);
        Assert.Equal(DnsResourceRecordTypeEnum.Cname, forItsClient.Answers[0].Type);
        Assert.Null(forUnknownAddress);
        Assert.Null(withNoAddressAtAll);
    }

    /// <summary>
    ///     This test proves a group-scoped rewrite applies to every member of that group, not just
    ///     one specific client.
    /// </summary>
    [Fact]
    public async Task GroupScopedRewrite_AppliesToEveryMemberOfTheGroup()
    {
        Guid groupId = Guid.NewGuid();
        FakeClientIdentityResolver resolver = new FakeClientIdentityResolver()
            .WithResolution(
                ClientOneAddress,
                new ClientIdentityResolution("aa:bb:cc:dd:ee:01", InternetProtocolVersionEnum.IPv4)
            )
            .WithResolution(
                ClientTwoAddress,
                new ClientIdentityResolution("aa:bb:cc:dd:ee:02", InternetProtocolVersionEnum.IPv4)
            );
        Fixture fixture = new(resolver);
        fixture.AddClient("aa:bb:cc:dd:ee:01", clientGroupId: groupId);
        fixture.AddClient("aa:bb:cc:dd:ee:02", clientGroupId: groupId);
        fixture.AddGroupScoped("kids.example.com", "restrict.example.net", groupId);

        DnsMessage forFirstMember = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("kids.example.com", DnsResourceRecordTypeEnum.A),
            ClientOneAddress
        );
        DnsMessage forSecondMember = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("kids.example.com", DnsResourceRecordTypeEnum.A),
            ClientTwoAddress
        );
        DnsMessage forNonMember = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("kids.example.com", DnsResourceRecordTypeEnum.A),
            UnknownAddress
        );

        Assert.NotNull(forFirstMember);
        Assert.NotNull(forSecondMember);
        Assert.Null(forNonMember);
    }

    /// <summary>
    ///     This test proves a disabled client contributes neither its own nor its group's scoped
    ///     rewrites -- mirroring <c>DnsFilterEngine</c>'s own identical "a disabled client
    ///     contributes nothing" rule.
    /// </summary>
    [Fact]
    public async Task DisabledClient_NeverMatchesAScopedRewrite()
    {
        FakeClientIdentityResolver resolver = new FakeClientIdentityResolver().WithResolution(
            ClientOneAddress,
            new ClientIdentityResolution("aa:bb:cc:dd:ee:01", InternetProtocolVersionEnum.IPv4)
        );
        Fixture fixture = new(resolver);
        DnsClientEntity client = fixture.AddClient("aa:bb:cc:dd:ee:01", isDisabled: true);
        fixture.AddClientScoped("kid-device.example.com", "restrict.example.net", client.Id);

        DnsMessage response = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("kid-device.example.com", DnsResourceRecordTypeEnum.A),
            ClientOneAddress
        );

        Assert.Null(response);
    }

    /// <summary>
    ///     This test proves the scope-specificity tie-break <c>DnsRewriteEngine.FindBestMatch</c>'s
    ///     own doc comment describes: when a global rewrite and a client-scoped rewrite both
    ///     exactly match the same query type for the resolved client, the client-scoped one wins
    ///     for that client -- while an unrelated client, which the scoped row never applies to,
    ///     still gets the global answer, exactly as it always would have.
    /// </summary>
    [Fact]
    public async Task ClientScopedRewrite_TakesPrecedenceOverAGlobalRewrite_ForItsOwnClientOnly()
    {
        FakeClientIdentityResolver resolver = new FakeClientIdentityResolver().WithResolution(
            ClientOneAddress,
            new ClientIdentityResolution("aa:bb:cc:dd:ee:01", InternetProtocolVersionEnum.IPv4)
        );
        Fixture fixture = new(resolver);
        DnsClientEntity client = fixture.AddClient("aa:bb:cc:dd:ee:01");
        fixture.Add("shared.example.com", "203.0.113.1");
        fixture.AddClientScoped("shared.example.com", "203.0.113.2", client.Id);

        DnsMessage forScopedClient = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("shared.example.com", DnsResourceRecordTypeEnum.A),
            ClientOneAddress
        );
        DnsMessage forUnrelatedClient = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("shared.example.com", DnsResourceRecordTypeEnum.A),
            UnknownAddress
        );

        Assert.Equal("203.0.113.2", new IPAddress(forScopedClient.Answers[0].Data).ToString());
        Assert.Equal("203.0.113.1", new IPAddress(forUnrelatedClient.Answers[0].Data).ToString());
    }
}
