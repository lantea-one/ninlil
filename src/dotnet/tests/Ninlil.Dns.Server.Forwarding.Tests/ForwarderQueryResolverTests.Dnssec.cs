using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Core.ClientSubnet;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Forwarding.Caching;
using Ninlil.Dns.Server.Forwarding.Tests.Fakes;
using Ninlil.Dns.Server.Recursive;
using Ninlil.Dns.Server.Recursive.Caching;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Tests.Support;
using Ninlil.Dns.Server.Recursive.Validation;
using FakeTrustAnchorStore = Ninlil.Dns.Server.Recursive.Tests.Fakes.FakeTrustAnchorStore;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This part of <see cref="ForwarderQueryResolverTests" /> tests DNSSEC validation of
///     forwarded answers and the per-client shape of the reply. It runs the real
///     <see cref="DnssecValidator" /> and <see cref="IterativeResolutionEngine" /> over the signed
///     <see cref="SignedHierarchy" /> (root, <c>com</c>, <c>example.com</c> and their children)
///     that the Recursive tests share. The upstream is <see cref="HierarchyForwarderClient" />, an
///     in-process forwarder client that answers the way a validating recursive server in front of
///     that hierarchy would, so a test can see exactly what the forwarder sent and what it
///     returned. Zones and forwarders live in the in-memory SQLite database of the main part.
/// </summary>
public sealed partial class ForwarderQueryResolverTests
{
    private const DnsResourceRecordTypeEnum A = DnsResourceRecordTypeEnum.A;

    /// <summary>
    ///     This class bundles one signed hierarchy, the validator that checks it and the fake
    ///     upstream that serves it, with the options a default install has (validation on,
    ///     recursion off).
    /// </summary>
    private sealed class DnssecEnvironment
    {
        public DnssecEnvironment()
        {
            Clock = new ManualTimeProvider();
            Hierarchy = SignedHierarchy.CreateStandard(Clock);
            Options = Hierarchy.CreateOptions();
            Options.EnableDnssecValidation = true;
            Options.EnableRecursion = false;

            StaticOptionsMonitor<RecursiveServerOptions> monitor = new(Options);
            RecursiveCache cache = new(monitor, new RecordingEventPublisher(), Clock);
            Engine = new IterativeResolutionEngine(cache, Hierarchy.Client, monitor);
            Validator = new DnssecValidator(
                Engine,
                new FakeTrustAnchorStore(Hierarchy.Anchors),
                ValidatorTestHelpers.CreateTrustCache(Clock),
                Clock
            );
            Upstream = new HierarchyForwarderClient(Hierarchy);
        }

        public ManualTimeProvider Clock { get; }
        public SignedHierarchy Hierarchy { get; }
        public RecursiveServerOptions Options { get; }
        public IterativeResolutionEngine Engine { get; }
        public DnssecValidator Validator { get; }
        public HierarchyForwarderClient Upstream { get; }
    }

    /// <summary>
    ///     This class is a forwarder client that answers from a <see cref="SignedHierarchy" />
    ///     without a socket, the way a recursive upstream does: it follows CNAMEs across zones,
    ///     returns RRSIGs and NSEC proofs only when the query had the DO bit, and, when the query
    ///     had EDNS, adds an OPT record that carries options a client must never see (a cookie,
    ///     padding and an ECS scope). It records every query it receives.
    /// </summary>
    private sealed class HierarchyForwarderClient(SignedHierarchy hierarchy)
        : IUpstreamForwarderClient
    {
        /// <summary>The upstream's own AD bit on every answer, which the forwarder must ignore.</summary>
        public bool SetAuthenticData { get; set; }

        /// <summary>Changes the answer before it is sent, for forged-data tests.</summary>
        public Action<DnsMessage> Tamper { get; set; }

        /// <summary>Makes every query fail, as an unreachable upstream does.</summary>
        public bool Fail { get; set; }

        /// <summary>Every query received, in order.</summary>
        public List<DnsMessage> Queries { get; } = [];

        public UpstreamForwarderProtocolEnum Protocol => UpstreamForwarderProtocolEnum.Plain;

        public Task<DnsMessage> ForwardAsync(
            UpstreamForwarderDefinition definition,
            DnsMessage query,
            CancellationToken stoppingToken = default
        )
        {
            Queries.Add(query);

            if (Fail)
                throw new IOException("The fake upstream is down.");

            DnsQuestion question = query.Questions[0];
            DnsMessage response = new()
            {
                Id = query.Id,
                IsResponse = true,
                RecursionDesired = query.RecursionDesired,
                RecursionAvailable = true,
                CheckingDisabled = query.CheckingDisabled,
                AuthenticData = SetAuthenticData,
                Questions = [question],
            };

            DnsName name = question.Name;

            for (int hop = 0; hop < 8; hop++)
            {
                DnsMessage step = hierarchy.Respond(ServerFor(name), name, question.Type);
                response.ResponseCode = step.ResponseCode;
                response.Answers.AddRange(step.Answers);
                response.AuthorityRecords.Clear();
                response.AuthorityRecords.AddRange(step.AuthorityRecords);

                DnsResourceRecord cname = step.Answers.FirstOrDefault(record =>
                    record.Type == DnsResourceRecordTypeEnum.Cname
                    && question.Type != DnsResourceRecordTypeEnum.Cname
                );

                if (cname is null || step.Answers.Any(record => record.Type == question.Type))
                    break;

                name = (
                    (CnameRecordData)DnsRecordDataCodecRegistry.Decode(cname.Type, cname.Data)
                ).Target;
            }

            bool dnssecOk = query.Edns?.DnssecOk == true;

            if (!dnssecOk)
            {
                response.Answers.RemoveAll(IsDnssecRecord);
                response.AuthorityRecords.RemoveAll(IsDnssecRecord);
            }

            if (query.Edns is not null)
                response.Edns = new DnsEdnsOptions(
                    0,
                    4096,
                    dnssecOk,
                    [
                        new DnsEdnsOption((ushort)EdnsOptionCodeEnum.Cookie, new byte[16]),
                        new DnsEdnsOption((ushort)EdnsOptionCodeEnum.Padding, new byte[8]),
                        new DnsEdnsOption(
                            (ushort)EdnsOptionCodeEnum.ClientSubnet,
                            [0, 1, 24, 24, 192, 0, 2]
                        ),
                    ]
                );

            Tamper?.Invoke(response);

            return Task.FromResult(response);
        }

        private IPAddress ServerFor(DnsName name) =>
            hierarchy
                .Zones.Where(zone => IsAtOrBelow(name, zone.Apex))
                .OrderByDescending(zone => zone.Apex.Labels.Count)
                .First()
                .Server;
    }

    private static bool IsAtOrBelow(DnsName name, DnsName ancestor)
    {
        int skip = name.Labels.Count - ancestor.Labels.Count;

        return skip >= 0 && new DnsName([.. name.Labels.Skip(skip)]) == ancestor;
    }

    private static bool IsDnssecRecord(DnsResourceRecord record) =>
        record.Type
            is DnsResourceRecordTypeEnum.RrSig
                or DnsResourceRecordTypeEnum.Nsec
                or DnsResourceRecordTypeEnum.Nsec3;

    /// <summary>
    ///     This method builds a resolver for <paramref name="environment" /> whose only forwarder
    ///     is the environment's fake upstream, and seeds that forwarder.
    /// </summary>
    private async Task<ForwarderQueryResolver> CreateDnssecResolverAsync(
        DnssecEnvironment environment,
        IForwardedResponseCache responseCache = null,
        ForwardingOptions forwardingOptions = null
    )
    {
        await SeedForwarderAsync("192.0.2.53");

        return CreateResolver(
            forwardingOptions: forwardingOptions,
            recursiveOptions: environment.Options,
            validator: environment.Validator,
            responseCache: responseCache,
            forwarderClients: [environment.Upstream]
        );
    }

    private static DnsMessage BuildClientQuery(
        string name,
        DnsResourceRecordTypeEnum type = A,
        bool edns = false,
        bool dnssecOk = false,
        bool checkingDisabled = false,
        bool authenticData = false
    )
    {
        DnsMessage query = new()
        {
            Id = 77,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            CheckingDisabled = checkingDisabled,
            AuthenticData = authenticData,
            Questions = [new DnsQuestion(DnsName.Parse(name), type, DnsClassEnum.Internet)],
        };

        if (edns || dnssecOk)
            query.Edns = new DnsEdnsOptions(0, 1232, dnssecOk, []);

        return query;
    }

    private static DnsResourceRecordTypeEnum CoveredType(DnsResourceRecord signature) =>
        (
            (RrSigRecordData)DnsRecordDataCodecRegistry.Decode(signature.Type, signature.Data)
        ).TypeCovered;

    [Fact]
    public async Task Dnssec_WithTheDefaultOptions_ASignedForwardedAnswerIsHandled()
    {
        DnssecEnvironment environment = new();
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsQueryResolution resolution = await resolver.TryResolveAsync(
            BuildContext(BuildClientQuery("www.example.com"))
        );

        Assert.True(resolution.IsHandled);
        Assert.Equal(DnsResponseCodeEnum.NoError, resolution.Response.ResponseCode);
        Assert.Contains(resolution.Response.Answers, record => record.Type == A);
    }

    [Fact]
    public async Task Dnssec_WithTheDefaultOptions_ASignedAnswerForADoClientIsSecure()
    {
        DnssecEnvironment environment = new();
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsQueryResolution resolution = await resolver.TryResolveAsync(
            BuildContext(BuildClientQuery("www.example.com", dnssecOk: true))
        );

        Assert.True(resolution.IsHandled);
        Assert.True(resolution.Response.AuthenticData);
        Assert.Contains(
            resolution.Response.Answers,
            record => record.Type == DnsResourceRecordTypeEnum.RrSig
        );
    }

    private static Task<DnsQueryResolution> AskAsync(
        ForwarderQueryResolver resolver,
        DnsMessage query
    ) => resolver.TryResolveAsync(BuildContext(query)).AsTask();

    private static async Task<DnsMessage> AskHandledAsync(
        ForwarderQueryResolver resolver,
        DnsMessage query
    )
    {
        DnsQueryResolution resolution = await AskAsync(resolver, query);

        Assert.True(resolution.IsHandled);

        return resolution.Response;
    }

    /// <summary>
    ///     This method corrupts the signature of the first RRSIG that covers
    ///     <paramref name="coveredType" /> in the answer section.
    /// </summary>
    private static void CorruptSignature(DnsMessage message, DnsResourceRecordTypeEnum coveredType)
    {
        int index = message.Answers.FindIndex(record =>
            record.Type == DnsResourceRecordTypeEnum.RrSig && CoveredType(record) == coveredType
        );
        DnsResourceRecord original = message.Answers[index];
        RrSigRecordData data = (RrSigRecordData)
            DnsRecordDataCodecRegistry.Decode(original.Type, original.Data);
        byte[] signature = [.. data.Signature];
        signature[0] ^= 0xFF;
        message.Answers[index] = new DnsResourceRecord(
            original.Name,
            original.Type,
            original.Class,
            original.TimeToLive,
            DnsRecordDataCodecRegistry.Encode(
                original.Type,
                new RrSigRecordData(
                    data.TypeCovered,
                    data.Algorithm,
                    data.Labels,
                    data.OriginalTimeToLiveSeconds,
                    data.SignatureExpiration,
                    data.SignatureInception,
                    data.KeyTag,
                    data.SignerName,
                    signature
                )
            )
        );
    }

    [Fact]
    public async Task Dnssec_TheQuerySentUpstream_HasDoAndCdAndNoTsigOrClientOptions()
    {
        DnssecEnvironment environment = new();
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsMessage request = BuildClientQuery("www.example.com");
        request.Edns = new DnsEdnsOptions(
            0,
            4000,
            false,
            [new DnsEdnsOption((ushort)EdnsOptionCodeEnum.Cookie, new byte[8])]
        );
        request.Tsig = new DnsTsigOptions(
            DnsName.Parse("client-key.example."),
            DnsTsigOptions.HmacSha256AlgorithmName,
            1,
            300,
            new byte[32],
            77,
            DnsResponseCodeEnum.NoError,
            []
        );

        await AskHandledAsync(resolver, request);

        DnsMessage sent = Assert.Single(environment.Upstream.Queries);
        Assert.Null(sent.Tsig);
        Assert.True(sent.CheckingDisabled);
        Assert.NotNull(sent.Edns);
        Assert.True(sent.Edns.DnssecOk);
        Assert.Equal(4000, sent.Edns.RequestorPayloadSize);
        Assert.Empty(sent.Edns.Options);
        Assert.Equal(request.Id, sent.Id);
        Assert.Equal(request.Questions[0].Name, sent.Questions[0].Name);

        // The shared request keeps its TSIG and flags: the pipeline signs the reply from it.
        Assert.NotNull(request.Tsig);
        Assert.False(request.CheckingDisabled);
        Assert.False(request.Edns.DnssecOk);
    }

    [Fact]
    public async Task Dnssec_AClientWithoutEdns_GetsNoOptAndNoDnssecRecords()
    {
        DnssecEnvironment environment = new();
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsMessage response = await AskHandledAsync(resolver, BuildClientQuery("www.example.com"));

        Assert.Null(response.Edns);
        Assert.False(response.AuthenticData);
        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Contains(response.Answers, record => record.Type == A);
        Assert.DoesNotContain(response.Answers, IsDnssecRecord);
        Assert.DoesNotContain(response.AuthorityRecords, IsDnssecRecord);
    }

    [Fact]
    public async Task Dnssec_AClientWithEdnsButWithoutDo_GetsAFreshOptAndNoDnssecRecords()
    {
        DnssecEnvironment environment = new();
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery("www.example.com", edns: true)
        );

        Assert.NotNull(response.Edns);
        Assert.False(response.Edns.DnssecOk);
        Assert.Equal(
            Ninlil.Dns.Server.Core.Resolution.DnsQueryDispatcher.DefaultUdpPayloadSizeOctets,
            response.Edns.RequestorPayloadSize
        );
        Assert.Empty(response.Edns.Options);
        Assert.False(response.AuthenticData);
        Assert.DoesNotContain(response.Answers, IsDnssecRecord);
    }

    [Fact]
    public async Task Dnssec_ADoClient_GetsSignaturesAdAndAnOptWithoutUpstreamOptions()
    {
        DnssecEnvironment environment = new();
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery("www.example.com", dnssecOk: true)
        );

        Assert.True(response.AuthenticData);
        Assert.Contains(response.Answers, record => record.Type == DnsResourceRecordTypeEnum.RrSig);
        Assert.NotNull(response.Edns);
        Assert.True(response.Edns.DnssecOk);
        Assert.Empty(response.Edns.Options);
    }

    [Fact]
    public async Task Dnssec_AClientThatOnlySetsAd_GetsAdButNoSignatures()
    {
        DnssecEnvironment environment = new();
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery("www.example.com", authenticData: true)
        );

        Assert.True(response.AuthenticData);
        Assert.DoesNotContain(response.Answers, IsDnssecRecord);
    }

    [Fact]
    public async Task Dnssec_UpstreamAdOnAnInsecureAnswer_IsCleared()
    {
        DnssecEnvironment environment = new();
        environment.Upstream.SetAuthenticData = true;
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery("www.insecure.com", dnssecOk: true)
        );

        Assert.Contains(response.Answers, record => record.Type == A);
        Assert.False(response.AuthenticData);
    }

    [Fact]
    public async Task Dnssec_UpstreamAdWithValidationOff_IsCleared()
    {
        DnssecEnvironment environment = new();
        environment.Options.EnableDnssecValidation = false;
        environment.Upstream.SetAuthenticData = true;
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery("www.example.com", dnssecOk: true, authenticData: true)
        );

        Assert.Contains(response.Answers, record => record.Type == A);
        Assert.False(response.AuthenticData);
        Assert.False(environment.Upstream.Queries[0].CheckingDisabled);
    }

    [Fact]
    public async Task Dnssec_WithValidationOff_TheClientsOwnFlagsAreForwarded()
    {
        DnssecEnvironment environment = new();
        environment.Options.EnableDnssecValidation = false;
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        await AskHandledAsync(resolver, BuildClientQuery("www.example.com"));

        DnsMessage sent = Assert.Single(environment.Upstream.Queries);
        Assert.Null(sent.Edns);
        Assert.False(sent.CheckingDisabled);
    }

    [Theory]
    [InlineData("web.example.com")]
    [InlineData("toinsecure.example.com")]
    public async Task Dnssec_ACnameChainThatIsSignedOrEndsInAnUnsignedZone_IsAccepted(string name)
    {
        DnssecEnvironment environment = new();
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery(name, dnssecOk: true)
        );

        Assert.Contains(response.Answers, record => record.Type == DnsResourceRecordTypeEnum.Cname);
        Assert.Contains(response.Answers, record => record.Type == A);

        // A chain that ends in an unsigned zone is Insecure, so only the signed one gets AD.
        Assert.Equal(name == "web.example.com", response.AuthenticData);
    }

    [Fact]
    public async Task Dnssec_AForgedUnsignedCnameHopIntoASignedTarget_IsBogus()
    {
        DnssecEnvironment environment = new();
        environment.Upstream.Tamper = message =>
            message.Answers.RemoveAll(record =>
                record.Type == DnsResourceRecordTypeEnum.RrSig
                && CoveredType(record) == DnsResourceRecordTypeEnum.Cname
            );
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsQueryResolution resolution = await AskAsync(
            resolver,
            BuildClientQuery("web.example.com", dnssecOk: true)
        );

        Assert.False(resolution.IsHandled);
    }

    [Fact]
    public async Task Dnssec_ACnameToANameWithNoData_IsSecureWhenTheTargetsProofHolds()
    {
        DnssecEnvironment environment = new();
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery("web.example.com", DnsResourceRecordTypeEnum.Aaaa, dnssecOk: true)
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Contains(response.Answers, record => record.Type == DnsResourceRecordTypeEnum.Cname);
        Assert.Contains(
            response.AuthorityRecords,
            record => record.Type == DnsResourceRecordTypeEnum.Nsec
        );
        Assert.True(response.AuthenticData);
    }

    [Fact]
    public async Task Dnssec_ACnameToANameThatDoesNotExist_IsSecureWhenTheProofHolds()
    {
        DnssecEnvironment environment = new();
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery("alias.example.com", dnssecOk: true)
        );

        Assert.Equal(DnsResponseCodeEnum.NxDomain, response.ResponseCode);
        Assert.True(response.AuthenticData);
    }

    [Fact]
    public async Task Dnssec_ANegativeAnswerWithoutItsProof_IsBogus()
    {
        DnssecEnvironment environment = new();
        environment.Upstream.Tamper = message =>
            message.AuthorityRecords.RemoveAll(record =>
                record.Type is DnsResourceRecordTypeEnum.Nsec
                || (
                    record.Type == DnsResourceRecordTypeEnum.RrSig
                    && CoveredType(record) == DnsResourceRecordTypeEnum.Nsec
                )
            );
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsQueryResolution resolution = await AskAsync(
            resolver,
            BuildClientQuery("missing.example.com", dnssecOk: true)
        );

        Assert.False(resolution.IsHandled);
    }

    [Theory]
    [InlineData("missing.example.com", DnsResponseCodeEnum.NxDomain)]
    [InlineData("www.example.com", DnsResponseCodeEnum.NoError)]
    public async Task Dnssec_ASignedNegativeAnswer_IsSecure(string name, DnsResponseCodeEnum code)
    {
        DnssecEnvironment environment = new();
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        // The name exists for the NODATA case, so ask for a type it does not have.
        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery(
                name,
                code == DnsResponseCodeEnum.NoError ? DnsResourceRecordTypeEnum.Aaaa : A,
                dnssecOk: true
            )
        );

        Assert.Equal(code, response.ResponseCode);
        Assert.Empty(response.Answers);
        Assert.True(response.AuthenticData);
        Assert.Contains(
            response.AuthorityRecords,
            record => record.Type == DnsResourceRecordTypeEnum.Soa
        );
    }

    [Fact]
    public async Task Dnssec_AWildcardAnswerFromASignedZone_IsSecureAndKeepsItsProofForADoClient()
    {
        DnssecEnvironment environment = new();
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery("x.wild.example.com", dnssecOk: true)
        );

        Assert.True(response.AuthenticData);
        Assert.Contains(
            response.AuthorityRecords,
            record => record.Type == DnsResourceRecordTypeEnum.Nsec
        );
    }

    [Fact]
    public async Task Dnssec_AWildcardAnswerWithoutItsProof_IsBogus()
    {
        DnssecEnvironment environment = new();
        environment.Upstream.Tamper = message => message.AuthorityRecords.RemoveAll(IsDnssecRecord);
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsQueryResolution resolution = await AskAsync(
            resolver,
            BuildClientQuery("x.wild.example.com", dnssecOk: true)
        );

        Assert.False(resolution.IsHandled);
    }

    [Fact]
    public async Task Dnssec_AWildcardAnswerForAClientWithoutDo_CarriesNoProof()
    {
        DnssecEnvironment environment = new();
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery("x.wild.example.com")
        );

        Assert.Contains(response.Answers, record => record.Type == A);
        Assert.DoesNotContain(response.AuthorityRecords, IsDnssecRecord);
        Assert.False(response.AuthenticData);
    }

    [Fact]
    public async Task Dnssec_ABogusAnswerForAClientWithCd_IsReturnedWithoutAdAndNotCached()
    {
        DnssecEnvironment environment = new();
        environment.Upstream.Tamper = message => CorruptSignature(message, A);
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(
            environment,
            CreateResponseCache()
        );

        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery("www.example.com", dnssecOk: true, checkingDisabled: true)
        );

        Assert.Contains(response.Answers, record => record.Type == A);
        Assert.False(response.AuthenticData);
        Assert.True(response.CheckingDisabled);

        // The same name for a client without CD is a failure, not the cached CD answer.
        DnsQueryResolution withoutCd = await AskAsync(
            resolver,
            BuildClientQuery("www.example.com", dnssecOk: true)
        );
        Assert.False(withoutCd.IsHandled);

        DnsQueryResolution withCdAgain = await AskAsync(
            resolver,
            BuildClientQuery("www.example.com", dnssecOk: true, checkingDisabled: true)
        );
        Assert.True(withCdAgain.IsHandled);
        Assert.Equal(3, environment.Upstream.Queries.Count);
    }

    [Fact]
    public async Task Dnssec_ABogusAnswerForAClientWithoutCd_IsAFailedForwarder()
    {
        DnssecEnvironment environment = new();
        environment.Upstream.Tamper = message => CorruptSignature(message, A);
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsQueryResolution resolution = await AskAsync(
            resolver,
            BuildClientQuery("www.example.com", dnssecOk: true)
        );

        Assert.False(resolution.IsHandled);
    }

    [Fact]
    public async Task Dnssec_AnAnswerForAnUpstreamThatSignsWithTheWrongKey_IsRejectedByTheRealChain()
    {
        // The old test signed example.com with the root key; the hierarchy makes the same forgery
        // visible: the A record is swapped for another address while the signature stays.
        DnssecEnvironment environment = new();
        environment.Upstream.Tamper = message =>
        {
            int index = message.Answers.FindIndex(record => record.Type == A);
            message.Answers[index] = new DnsResourceRecord(
                message.Answers[index].Name,
                A,
                DnsClassEnum.Internet,
                message.Answers[index].TimeToLive,
                DnsRecordDataCodecRegistry.Encode(
                    A,
                    new ARecordData(IPAddress.Parse("203.0.113.7"))
                )
            );
        };
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsQueryResolution resolution = await AskAsync(
            resolver,
            BuildClientQuery("www.example.com", dnssecOk: true)
        );

        Assert.False(resolution.IsHandled);
    }

    [Fact]
    public async Task Dnssec_AnEcsOptionIsSentUpstreamButNeverReturnedToTheClient()
    {
        DnssecEnvironment environment = new();
        EdnsClientSubnetService subnetService = new(
            new StaticOptionsMonitor<EdnsClientSubnetOptions>(
                new EdnsClientSubnetOptions { Enable = true }
            )
        );
        await SeedForwarderAsync("192.0.2.53");
        ForwarderQueryResolver resolver = CreateResolver(
            recursiveOptions: environment.Options,
            validator: environment.Validator,
            forwarderClients: [environment.Upstream],
            clientSubnetService: subnetService
        );

        // A client without EDNS: the forwarder adds the OPT record upstream and drops it again.
        DnsMessage plain = await AskHandledAsync(resolver, BuildClientQuery("www.example.com"));

        Assert.Null(plain.Edns);
        DnsMessage sent = Assert.Single(environment.Upstream.Queries);
        Assert.Contains(
            sent.Edns.Options,
            option => option.Code == (ushort)EdnsOptionCodeEnum.ClientSubnet
        );
        Assert.True(sent.Edns.DnssecOk);
        Assert.True(sent.CheckingDisabled);

        DnsMessage withEdns = await AskHandledAsync(
            resolver,
            BuildClientQuery("www.example.com", edns: true)
        );

        Assert.Empty(withEdns.Edns.Options);
    }

    [Fact]
    public async Task Dnssec_ACachedAnswer_IsShapedForEachClientAndKeepsItsSecureFlag()
    {
        DnssecEnvironment environment = new();
        ForwardedResponseCache cache = CreateResponseCache();
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(
            environment,
            cache,
            _cachingForwardingOptions
        );

        // A DO client stores the answer under its own key.
        DnsMessage first = await AskHandledAsync(
            resolver,
            BuildClientQuery("www.example.com", dnssecOk: true)
        );
        Assert.True(first.AuthenticData);

        // A client without DO has its own key, so it goes upstream once and is then cached.
        DnsMessage plainMiss = await AskHandledAsync(resolver, BuildClientQuery("www.example.com"));
        DnsMessage plainHit = await AskHandledAsync(resolver, BuildClientQuery("www.example.com"));
        Assert.Equal(2, environment.Upstream.Queries.Count);

        foreach (DnsMessage plain in new[] { plainMiss, plainHit })
        {
            Assert.Null(plain.Edns);
            Assert.False(plain.AuthenticData);
            Assert.DoesNotContain(plain.Answers, IsDnssecRecord);
        }

        // The DO client's hit is served locally with signatures, AD and a clean OPT record.
        DnsMessage doHit = await AskHandledAsync(
            resolver,
            BuildClientQuery("www.example.com", dnssecOk: true)
        );
        Assert.Equal(2, environment.Upstream.Queries.Count);
        Assert.True(doHit.AuthenticData);
        Assert.Contains(doHit.Answers, record => record.Type == DnsResourceRecordTypeEnum.RrSig);
        Assert.Empty(doHit.Edns.Options);
    }

    [Fact]
    public async Task Dnssec_AStaleAnswer_NeverCarriesAd()
    {
        DnssecEnvironment environment = new();
        _cachingRecursiveOptions.EnableServeStale = true;
        _cachingRecursiveOptions.MaxStaleServeAge = TimeSpan.FromDays(1);
        ForwardedResponseCache cache = CreateResponseCache();
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(
            environment,
            cache,
            _cachingForwardingOptions
        );

        DnsMessage fresh = await AskHandledAsync(
            resolver,
            BuildClientQuery("www.example.com", dnssecOk: true)
        );
        Assert.True(fresh.AuthenticData);

        // Past the 300 second TTL, with the upstream down: the stale answer is served.
        _cacheClock.Advance(TimeSpan.FromMinutes(10));
        environment.Upstream.Fail = true;

        DnsMessage stale = await AskHandledAsync(
            resolver,
            BuildClientQuery("www.example.com", dnssecOk: true)
        );

        Assert.Contains(stale.Answers, record => record.Type == A);
        Assert.All(
            stale.Answers.Where(record => record.Type == A),
            record => Assert.Equal(ForwardedResponseCache.StaleAnswerTtlSeconds, record.TimeToLive)
        );
        Assert.False(stale.AuthenticData);
    }
}
