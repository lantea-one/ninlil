using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Recursive.Caching;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Tests.Fakes;
using Ninlil.Dns.Server.Recursive.Tests.Support;
using Ninlil.Dns.Server.Recursive.Validation;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests the Insecure domain list (negative trust anchors) through the recursive
///     path: the real <see cref="IterativeResolutionEngine" />, cache,
///     <see cref="DnssecValidator" /> and <see cref="RecursiveQueryResolver" /> over the
///     <see cref="SignedHierarchy" />, whose signed root proves that <c>internal</c> and
///     <c>lan</c> do not exist while a split-horizon referral still leads to the private
///     <c>corp.internal</c> and <c>corp.lan</c> servers. Validation is on, as by default.
/// </summary>
public sealed class RecursiveInsecureDomainsTests
{
    private const DnsResourceRecordTypeEnum A = DnsResourceRecordTypeEnum.A;

    private sealed class Environment
    {
        public Environment()
        {
            Clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
            Hierarchy = SignedHierarchy.CreateStandard(Clock);
            Options = CreateOptions();
            Monitor = new SwappableOptionsMonitor<RecursiveServerOptions>(Options);
            Cache = new RecursiveCache(Monitor, new RecordingEventPublisher(), Clock);
            Engine = new IterativeResolutionEngine(Cache, Hierarchy.Client, Monitor);
            Validator = new DnssecValidator(
                Engine,
                new FakeTrustAnchorStore(Hierarchy.Anchors),
                ValidatorTestHelpers.CreateTrustCache(Clock),
                Clock,
                insecureDomains: new InsecureDomainPolicy(Monitor)
            );
            Resolver = new RecursiveQueryResolver(Engine, Validator, Monitor);
        }

        public ManualTimeProvider Clock { get; }
        public SignedHierarchy Hierarchy { get; }
        public RecursiveServerOptions Options { get; private set; }
        public SwappableOptionsMonitor<RecursiveServerOptions> Monitor { get; }
        public RecursiveCache Cache { get; }
        public IterativeResolutionEngine Engine { get; }
        public DnssecValidator Validator { get; }
        public RecursiveQueryResolver Resolver { get; }

        private RecursiveServerOptions CreateOptions()
        {
            RecursiveServerOptions options = Hierarchy.CreateOptions();
            options.EnableRecursion = true;
            options.EnableDnssecValidation = true;

            return options;
        }

        /// <summary>
        ///     This method swaps in a new options object with exactly these Insecure domains.
        /// </summary>
        public void SetInsecureDomains(params string[] domains)
        {
            Options = CreateOptions();
            Options.UseCustomInsecureDomains = true;
            Options.InsecureDomains = [.. domains];
            Monitor.CurrentValue = Options;
        }

        public async Task<DnsMessage> AskAsync(string name)
        {
            DnsMessage request = new()
            {
                RecursionDesired = true,
                Questions = [new DnsQuestion(DnsName.Parse(name), A, DnsClassEnum.Internet)],
                Edns = new DnsEdnsOptions(0, 1232, true, []),
            };

            DnsQueryResolution resolution = await Resolver.TryResolveAsync(
                new DnsQueryContext(
                    request,
                    DnsTransportProtocolEnum.Udp,
                    IPAddress.Parse("203.0.113.5"),
                    Clock.GetUtcNow()
                )
            );

            Assert.True(resolution.IsHandled);

            return resolution.Response;
        }
    }

    [Fact]
    public async Task TryResolveAsync_APrivateNameOnTheDefaultList_IsServedWithoutAd()
    {
        Environment environment = new();

        DnsMessage response = await environment.AskAsync("www.corp.internal");

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Contains(response.Answers, record => record.Type == A);
        Assert.False(response.AuthenticData);
    }

    [Fact]
    public async Task TryResolveAsync_APrivateNameOffTheList_IsServFail()
    {
        Environment environment = new();

        DnsMessage response = await environment.AskAsync("www.corp.lan");

        Assert.Equal(DnsResponseCodeEnum.ServerFailure, response.ResponseCode);
    }

    [Fact]
    public async Task TryResolveAsync_AnEntryAddedToTheList_ServesTheNameWithoutAd()
    {
        Environment environment = new();
        Assert.Equal(
            DnsResponseCodeEnum.ServerFailure,
            (await environment.AskAsync("www.corp.lan")).ResponseCode
        );

        environment.SetInsecureDomains("corp.lan");

        DnsMessage response = await environment.AskAsync("www.corp.lan");
        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.False(response.AuthenticData);
    }

    [Fact]
    public async Task TryResolveAsync_AnEntryRemovedFromTheList_MakesTheNameBogusAgain()
    {
        Environment environment = new();
        Assert.Equal(
            DnsResponseCodeEnum.NoError,
            (await environment.AskAsync("www.corp.internal")).ResponseCode
        );

        environment.SetInsecureDomains("home.arpa");

        Assert.Equal(
            DnsResponseCodeEnum.ServerFailure,
            (await environment.AskAsync("www.corp.internal")).ResponseCode
        );
    }

    [Fact]
    public async Task TryResolveAsync_APublicSignedName_IsStillSecure()
    {
        Environment environment = new();

        DnsMessage response = await environment.AskAsync("www.example.com");

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.True(response.AuthenticData);
    }

    [Fact]
    public async Task TryResolveAsync_AStrippedSignatureOnAPublicSignedName_IsStillServFail()
    {
        Environment environment = new();
        DnsMessage genuine = environment.Hierarchy.Respond(
            SignedHierarchy.ExampleAddress,
            DnsName.Parse("www.example.com"),
            A
        );
        genuine.Answers.RemoveAll(record => record.Type == DnsResourceRecordTypeEnum.RrSig);
        environment.Hierarchy.Client.AddResponse(
            SignedHierarchy.ExampleAddress,
            DnsName.Parse("www.example.com"),
            A,
            genuine
        );

        DnsMessage response = await environment.AskAsync("www.example.com");

        Assert.Equal(DnsResponseCodeEnum.ServerFailure, response.ResponseCode);
    }

    [Fact]
    public async Task TryResolveAsync_ACnameFromAPublicNameIntoAListedDomain_IsStillValidated()
    {
        Environment environment = new();
        DnsMessage forged = environment.Hierarchy.Respond(
            SignedHierarchy.ExampleAddress,
            DnsName.Parse("www.example.com"),
            A
        );
        forged.Answers.Clear();
        forged.Answers.Add(
            new DnsResourceRecord(
                DnsName.Parse("www.example.com"),
                DnsResourceRecordTypeEnum.Cname,
                DnsClassEnum.Internet,
                300,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.Cname,
                    new CnameRecordData(DnsName.Parse("www.corp.internal"))
                )
            )
        );
        environment.Hierarchy.Client.AddResponse(
            SignedHierarchy.ExampleAddress,
            DnsName.Parse("www.example.com"),
            A,
            forged
        );

        DnsMessage response = await environment.AskAsync("www.example.com");

        Assert.Equal(DnsResponseCodeEnum.ServerFailure, response.ResponseCode);
    }
}
