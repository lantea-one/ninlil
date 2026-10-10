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

/// <summary>
///     This class covers <see cref="DnsRewriteEngine" />: exact and wildcard matching, which
///     record types each target kind answers, disabled rows, the wildcard match timeout, the live
///     <see cref="DnsRewriteOptions.Enable" /> switch, and client and group scoping. Each test
///     builds the engine over in-memory repositories in a small service provider, with a fixed
///     options monitor and, for scoping, <see cref="FakeClientIdentityResolver" />.
/// </summary>
public class DnsRewriteEngineTests
{
    internal sealed class Fixture
    {
        public readonly InMemoryDnsRewriteRepository Rewrites = new();
        public readonly InMemoryDnsClientRepository Clients = new();
        public readonly InMemoryDnsClientAddressRepository ClientAddresses = new();
        public readonly EntityChangeNotifier Notifier = new();
        public readonly DnsRewriteCache Cache;
        public readonly DnsClientDirectory Directory;
        public readonly DnsRewriteOptions Options = new() { Enable = true, TimeToLiveSeconds = 60 };
        public readonly DnsRewriteEngine Engine;
        public readonly IServiceScopeFactory ScopeFactory;

        /// <param name="clientIdentityResolver">
        ///     The optional resolver to build the engine with. <see langword="null" /> (the default)
        ///     leaves client scoping off.
        /// </param>
        /// <param name="registerClientRepository">
        ///     Whether <see cref="Clients" /> is registered in the engine's service provider.
        ///     <see langword="false" /> sets up the case of a resolver without a client repository.
        /// </param>
        public Fixture(
            IClientIdentityResolver clientIdentityResolver = null,
            bool registerClientRepository = true
        )
        {
            ServiceCollection services = new();
            services.AddSingleton<IDnsRewriteRepository>(Rewrites);

            if (registerClientRepository)
            {
                services.AddSingleton<IDnsClientRepository>(Clients);
                services.AddSingleton<IDnsClientAddressRepository>(ClientAddresses);
            }

            IServiceScopeFactory scopeFactory = services
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>();
            ScopeFactory = scopeFactory;

            Cache = new DnsRewriteCache(
                scopeFactory,
                Notifier,
                NullLogger<DnsRewriteCache>.Instance
            );
            Directory = new DnsClientDirectory(
                scopeFactory,
                Notifier,
                NullLogger<DnsClientDirectory>.Instance
            );
            Engine = new DnsRewriteEngine(
                Cache,
                new StaticOptionsMonitor<DnsRewriteOptions>(Options),
                NullLogger<DnsRewriteEngine>.Instance,
                clientIdentityResolver,
                registerClientRepository ? Directory : null
            );
        }

        /// <summary>
        ///     This method stands in for the committed save a real repository write ends with: the
        ///     in-memory repositories have no unit of work, so the fixture's setup helpers raise
        ///     the signal themselves, for the entity types the caches watch.
        /// </summary>
        public void Changed() =>
            Notifier.NotifyChanged([
                typeof(DnsRewriteEntity),
                typeof(DnsClientEntity),
                typeof(DnsClientAddressEntity),
            ]);

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
            Changed();
            return entity;
        }

        /// <summary>
        ///     This method adds a rewrite scoped directly to a single client.
        /// </summary>
        public DnsRewriteEntity AddClientScoped(string domain, string target, Guid clientId)
        {
            DnsRewriteEntity entity = Add(domain, target);
            entity.ClientId = clientId;
            Changed();
            return entity;
        }

        /// <summary>
        ///     This method adds a rewrite scoped to a single client group.
        /// </summary>
        public DnsRewriteEntity AddGroupScoped(string domain, string target, Guid clientGroupId)
        {
            DnsRewriteEntity entity = Add(domain, target);
            entity.ClientGroupId = clientGroupId;
            Changed();
            return entity;
        }

        /// <summary>
        ///     This method registers a client for an IPv4 client identifier, optionally in a client
        ///     group or disabled.
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
    }

    internal static DnsMessage BuildRequest(string name, DnsResourceRecordTypeEnum type) =>
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
        // Many '*' segments become many ".*" segments, which backtrack badly on a long
        // non-matching name. DnsFilterEngineTests has the same test for the filter's regexes.
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
    ///     This test checks that an engine built while <see cref="DnsRewriteOptions.Enable" /> is
    ///     off starts answering, without being rebuilt, once the flag turns on, because it reads
    ///     <c>CurrentValue</c> on every call.
    /// </summary>
    [Fact]
    public async Task TryBuildResponseAsync_WhenEnableFlipsLiveOnTheSharedOptionsObject_TheSameEngineInstanceStartsAnswering()
    {
        Fixture fixture = new();
        fixture.Options.Enable = false;
        fixture.Add("host.example.com", "203.0.113.5");

        // A matching rewrite exists, so a null here means "disabled", not "no match".
        DnsMessage before = await fixture.Engine.TryBuildResponseAsync(
            BuildRequest("host.example.com", DnsResourceRecordTypeEnum.A)
        );
        Assert.Null(before);

        fixture.Options.Enable = true;

        // The same instance now answers.
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
    ///     This test checks that a global rewrite applies to every query, both a resolved client's
    ///     and an unknown address's, when an <see cref="IClientIdentityResolver" /> is present.
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
    ///     This test checks that a global rewrite applies when no client-identity resolver is
    ///     wired up, even though a <c>remoteAddress</c> is passed.
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
    ///     This test checks that a global rewrite applies when a resolver is present but no client
    ///     repository is registered.
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
    ///     This test checks that a client-scoped rewrite answers only that client; an unresolved
    ///     address gets no answer for the same question.
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
    ///     This test checks that a group-scoped rewrite applies to every member of the group.
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
    ///     This test checks that a disabled client does not match a rewrite scoped to it (a
    ///     disabled client resolves to nothing, as in <c>DnsFilterEngine</c>).
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
    ///     This test checks the audience tie-break in <c>DnsRewriteEngine.FindBestMatch</c>: when a
    ///     global and a client-scoped rewrite both match exactly, the client-scoped one wins for
    ///     that client, and an unknown address gets the global answer.
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
