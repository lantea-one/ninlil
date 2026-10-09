using System.Net;
using Microsoft.Extensions.Logging;
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
///     This class tests the response <see cref="RecursiveQueryResolver" /> builds around DNSSEC
///     validation: which records a client sees, when AD is set, and that a wildcard answer from a
///     signed zone validates. It runs the real <see cref="IterativeResolutionEngine" />, a real
///     cache and the real <see cref="DnssecValidator" /> over the fake signed
///     <see cref="SignedHierarchy" />, so the engine's record retention is part of what is tested.
/// </summary>
public sealed class RecursiveQueryResolverDnssecTests
{
    private const DnsResourceRecordTypeEnum A = DnsResourceRecordTypeEnum.A;

    private sealed class Environment
    {
        public Environment(SignedHierarchy hierarchy = null, ILogger<DnssecValidator> logger = null)
        {
            Clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
            Hierarchy = hierarchy ?? SignedHierarchy.CreateStandard(Clock);
            Options = Hierarchy.CreateOptions();
            Options.EnableRecursion = true;
            StaticOptionsMonitor<RecursiveServerOptions> monitor = new(Options);
            Cache = new RecursiveCache(monitor, new RecordingEventPublisher(), Clock);
            Engine = new IterativeResolutionEngine(Cache, Hierarchy.Client, monitor);
            Validator = new DnssecValidator(
                Engine,
                new FakeTrustAnchorStore(Hierarchy.Anchors),
                ValidatorTestHelpers.CreateTrustCache(Clock),
                Clock,
                logger
            );
            Resolver = new RecursiveQueryResolver(Engine, Validator, monitor);
        }

        public ManualTimeProvider Clock { get; }
        public SignedHierarchy Hierarchy { get; }
        public RecursiveServerOptions Options { get; }
        public RecursiveCache Cache { get; }
        public IterativeResolutionEngine Engine { get; }
        public DnssecValidator Validator { get; }
        public RecursiveQueryResolver Resolver { get; }

        /// <summary>
        ///     This method asks the resolver one question as a client with the given flags.
        /// </summary>
        public async Task<DnsMessage> AskAsync(
            string name,
            DnsResourceRecordTypeEnum type,
            bool dnssecOk = false,
            bool authenticData = false,
            bool checkingDisabled = false
        )
        {
            DnsMessage request = new()
            {
                RecursionDesired = true,
                AuthenticData = authenticData,
                CheckingDisabled = checkingDisabled,
                Questions = [new DnsQuestion(DnsName.Parse(name), type, DnsClassEnum.Internet)],
            };

            if (dnssecOk)
                request.Edns = new DnsEdnsOptions(0, 1232, true, []);

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

    private static DnsResourceRecordTypeEnum CoveredType(DnsResourceRecord signature) =>
        (
            (RrSigRecordData)DnsRecordDataCodecRegistry.Decode(signature.Type, signature.Data)
        ).TypeCovered;

    [Fact]
    public async Task TryResolveAsync_AWildcardAnswerFromASignedZone_IsSecureAndNotServFail()
    {
        Environment environment = new();

        DnsMessage response = await environment.AskAsync("x.wild.example.com", A, dnssecOk: true);

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.True(response.AuthenticData);
        Assert.Contains(response.Answers, record => record.Type == A);
    }

    [Fact]
    public async Task TryResolveAsync_AWildcardAnswerForADoClient_PutsTheNsecProofInAuthorityNotAnswers()
    {
        Environment environment = new();

        DnsMessage response = await environment.AskAsync("x.wild.example.com", A, dnssecOk: true);

        Assert.DoesNotContain(
            response.Answers,
            record =>
                record.Type == DnsResourceRecordTypeEnum.Nsec
                || (
                    record.Type == DnsResourceRecordTypeEnum.RrSig
                    && CoveredType(record) == DnsResourceRecordTypeEnum.Nsec
                )
        );
        Assert.Contains(response.Answers, record => record.Type == DnsResourceRecordTypeEnum.RrSig);
        Assert.Contains(
            response.AuthorityRecords,
            record => record.Type == DnsResourceRecordTypeEnum.Nsec
        );
        Assert.Contains(
            response.AuthorityRecords,
            record =>
                record.Type == DnsResourceRecordTypeEnum.RrSig
                && CoveredType(record) == DnsResourceRecordTypeEnum.Nsec
        );
    }

    [Fact]
    public async Task TryResolveAsync_AWildcardAnswerForANonDoClient_CarriesNoDnssecRecords()
    {
        Environment environment = new();

        DnsMessage response = await environment.AskAsync("x.wild.example.com", A);

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.All(response.Answers, record => Assert.Equal(A, record.Type));
        Assert.Empty(response.AuthorityRecords);
    }

    [Fact]
    public async Task TryResolveAsync_ACachedWildcardAnswer_IsStillSecureWithItsProof()
    {
        Environment environment = new();

        await environment.AskAsync("x.wild.example.com", A, dnssecOk: true);
        DnsMessage second = await environment.AskAsync("x.wild.example.com", A, dnssecOk: true);

        Assert.Equal(DnsResponseCodeEnum.NoError, second.ResponseCode);
        Assert.True(second.AuthenticData);
        Assert.Contains(
            second.AuthorityRecords,
            record => record.Type == DnsResourceRecordTypeEnum.Nsec
        );
    }

    [Fact]
    public async Task TryResolveAsync_ASecureAnswerForAClientWithoutDoOrAd_LeavesAdClear()
    {
        Environment environment = new();

        DnsMessage response = await environment.AskAsync("www.example.com", A);

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.False(response.AuthenticData);
    }

    [Fact]
    public async Task TryResolveAsync_ASecureAnswerForADoClient_SetsAd()
    {
        Environment environment = new();

        DnsMessage response = await environment.AskAsync("www.example.com", A, dnssecOk: true);

        Assert.True(response.AuthenticData);
    }

    [Fact]
    public async Task TryResolveAsync_ASecureAnswerForAnAdClientWithoutDo_SetsAd()
    {
        Environment environment = new();

        DnsMessage response = await environment.AskAsync("www.example.com", A, authenticData: true);

        Assert.True(response.AuthenticData);
        Assert.All(response.Answers, record => Assert.Equal(A, record.Type));
    }

    [Fact]
    public async Task TryResolveAsync_AnInsecureAnswerForADoClient_LeavesAdClear()
    {
        Environment environment = new();

        DnsMessage response = await environment.AskAsync("www.insecure.com", A, dnssecOk: true);

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.False(response.AuthenticData);
    }

    /// <summary>
    ///     This class is an <see cref="ILogger{TCategoryName}" /> that keeps the Warning messages.
    /// </summary>
    private sealed class WarningLogger : ILogger<DnssecValidator>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task TryResolveAsync_ABogusZone_IsLoggedWithItsReasonOncePerHour()
    {
        WarningLogger logger = new();
        Environment environment = new(logger: logger);
        environment.Hierarchy.Client.AddResponse(
            SignedHierarchy.ComAddress,
            DnsName.Parse("example.com"),
            DnsResourceRecordTypeEnum.DS,
            new DnsMessage { IsResponse = true, ResponseCode = DnsResponseCodeEnum.ServerFailure }
        );

        DnsMessage first = await environment.AskAsync("www.example.com", A);
        await environment.AskAsync("www.example.com", A);

        Assert.Equal(DnsResponseCodeEnum.ServerFailure, first.ResponseCode);
        string warning = Assert.Single(logger.Warnings);
        Assert.Contains("example.com", warning);
        Assert.Contains("DS", warning);

        environment.Clock.Advance(TimeSpan.FromMinutes(61));
        await environment.AskAsync("www.example.com", A);

        Assert.Equal(2, logger.Warnings.Count);
    }
}
