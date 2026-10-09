using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Core.Caching;
using Ninlil.Dns.Server.Recursive.Caching;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Tests.Fakes;
using Ninlil.Dns.Server.Recursive.Tests.Support;
using Ninlil.Dns.Server.Recursive.Validation;

namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests the chain of trust of <see cref="DnssecValidator" /> with the real
///     <see cref="IterativeResolutionEngine" />, a real <see cref="RecursiveCache" /> and a
///     fake upstream that serves the signed zones of <see cref="SignedHierarchy" />: signed root,
///     <c>com</c> and <c>example.com</c>, an unsigned child and two children on the same server as
///     their parent. Each test resolves a name as the resolver does and validates the result with
///     the same budget.
/// </summary>
public sealed class DnssecValidatorChainTests
{
    private sealed class Environment
    {
        public Environment(
            bool minimize = false,
            TimeSpan? exampleSignatureLifetime = null,
            SignedHierarchy hierarchy = null,
            ITrustAnchorStore anchorStore = null
        )
        {
            Clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
            Hierarchy =
                hierarchy ?? SignedHierarchy.CreateStandard(Clock, exampleSignatureLifetime);
            Options = Hierarchy.CreateOptions(minimize);
            StaticOptionsMonitor<RecursiveServerOptions> monitor = new(Options);
            Cache = new RecursiveCache(monitor, new RecordingEventPublisher(), Clock);
            Engine = new IterativeResolutionEngine(Cache, Hierarchy.Client, monitor);
            TrustCache = ValidatorTestHelpers.CreateTrustCache(Clock);
            Validator = new DnssecValidator(
                Engine,
                anchorStore ?? new FakeTrustAnchorStore(Hierarchy.Anchors),
                TrustCache,
                Clock
            );
        }

        public ManualTimeProvider Clock { get; }
        public SignedHierarchy Hierarchy { get; }
        public RecursiveServerOptions Options { get; }
        public RecursiveCache Cache { get; }
        public IterativeResolutionEngine Engine { get; }
        public DnssecTrustCache TrustCache { get; }
        public DnssecValidator Validator { get; }

        /// <summary>
        ///     This method resolves a name with DNSSEC data and validates the result, the way
        ///     <see cref="RecursiveQueryResolver" /> does, sharing one budget.
        /// </summary>
        public async Task<(
            IterativeResolutionResult Result,
            DnssecValidationResult Validation
        )> QueryAsync(string name, DnsResourceRecordTypeEnum type)
        {
            DnsName queried = DnsName.Parse(name);
            QueryBudget budget = new(Options);
            IterativeResolutionResult result = await Engine.ResolveAsync(
                queried,
                type,
                DnsClassEnum.Internet,
                true,
                budget,
                false
            );
            DnssecValidationResult validation = await Validator.ValidateAsync(
                queried,
                type,
                DnsClassEnum.Internet,
                result,
                budget
            );

            return (result, validation);
        }
    }

    private static readonly DnsResourceRecordTypeEnum A = DnsResourceRecordTypeEnum.A;

    private static void AssertState(
        DnssecValidationStateEnum expected,
        DnssecValidationResult actual
    ) => Assert.True(actual.State == expected, $"{actual.State}: {actual.Reason}");

    [Fact]
    public async Task ValidateAsync_TheSameSignedAnswerTwice_IsSecureBothTimes()
    {
        Environment environment = new();

        (_, DnssecValidationResult first) = await environment.QueryAsync("www.example.com", A);
        (_, DnssecValidationResult second) = await environment.QueryAsync("www.example.com", A);

        AssertState(DnssecValidationStateEnum.Secure, first);
        AssertState(DnssecValidationStateEnum.Secure, second);
    }

    [Fact]
    public async Task ValidateAsync_TheSecondValidation_SendsNoQueryAndSpendsOneSignatureCheck()
    {
        Environment environment = new();
        (_, DnssecValidationResult first) = await environment.QueryAsync("www.example.com", A);
        int queriesAfterFirst = environment.Hierarchy.Client.QueryCount;

        (_, DnssecValidationResult second) = await environment.QueryAsync("www.example.com", A);

        Assert.True(first.SignatureChecksUsed > 1);
        Assert.Equal(queriesAfterFirst, environment.Hierarchy.Client.QueryCount);
        Assert.Equal(1, second.SignatureChecksUsed);
    }

    [Fact]
    public async Task ValidateAsync_WithQnameMinimization_IsSecure()
    {
        Environment environment = new(minimize: true);

        (_, DnssecValidationResult validation) = await environment.QueryAsync("www.example.com", A);

        AssertState(DnssecValidationStateEnum.Secure, validation);
    }

    [Fact]
    public async Task ValidateAsync_AnAnswerInAnUnsignedDelegation_IsInsecure()
    {
        Environment environment = new();

        (_, DnssecValidationResult validation) = await environment.QueryAsync(
            "www.insecure.com",
            A
        );

        AssertState(DnssecValidationStateEnum.Insecure, validation);
    }

    [Fact]
    public async Task ValidateAsync_ASignedChildServedByTheParentsServer_IsSecure()
    {
        Environment environment = new();

        (IterativeResolutionResult result, DnssecValidationResult validation) =
            await environment.QueryAsync("www.sub.example.com", A);

        // No referral is seen, so the last zone cut is the parent: the signer name finds the
        // child.
        Assert.Equal(DnsName.Parse("example.com"), result.ZoneCutsTraversed[^1]);
        AssertState(DnssecValidationStateEnum.Secure, validation);
    }

    [Fact]
    public async Task ValidateAsync_AnUnsignedChildServedByTheParentsServer_IsInsecureThroughTheWalk()
    {
        Environment environment = new();

        (IterativeResolutionResult result, DnssecValidationResult validation) =
            await environment.QueryAsync("www.plain.example.com", A);

        Assert.Equal(DnsName.Parse("example.com"), result.ZoneCutsTraversed[^1]);
        AssertState(DnssecValidationStateEnum.Insecure, validation);
    }

    [Fact]
    public async Task ValidateAsync_AnUnsignedAnswerWithNoZoneCuts_IsInsecureThroughTheWalkFromTheRoot()
    {
        Environment environment = new();
        (IterativeResolutionResult result, _) = await environment.QueryAsync("www.insecure.com", A);

        // The forwarder builds results without zone cuts.
        DnssecValidationResult validation = await environment.Validator.ValidateAsync(
            DnsName.Parse("www.insecure.com"),
            A,
            DnsClassEnum.Internet,
            result with
            {
                ZoneCutsTraversed = [],
            },
            new QueryBudget(environment.Options)
        );

        AssertState(DnssecValidationStateEnum.Insecure, validation);
    }

    [Fact]
    public async Task ValidateAsync_ACnameChainWithinASignedZone_IsSecure()
    {
        Environment environment = new();

        (IterativeResolutionResult result, DnssecValidationResult validation) =
            await environment.QueryAsync("web.example.com", A);

        Assert.Single(result.CnameChain);
        AssertState(DnssecValidationStateEnum.Secure, validation);
    }

    [Fact]
    public async Task ValidateAsync_ASignedCnameIntoAnInsecureZone_IsInsecure()
    {
        Environment environment = new();

        (_, DnssecValidationResult validation) = await environment.QueryAsync(
            "toinsecure.example.com",
            A
        );

        AssertState(DnssecValidationStateEnum.Insecure, validation);
    }

    [Fact]
    public async Task ValidateAsync_ACnameHopInAZoneWithAnUnsupportedDsDigest_IsInsecure()
    {
        ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
        SignedHierarchy hierarchy = new(clock);
        SignedHierarchy.Zone root = hierarchy.AddZone(
            ".",
            SignedHierarchy.RootAddress,
            true,
            "a.root.test"
        );
        SignedHierarchy.Zone com = hierarchy.AddZone("com", SignedHierarchy.ComAddress, true);
        SignedHierarchy.Zone gost = hierarchy.AddZone(
            "gost.com",
            SignedHierarchy.ExampleAddress,
            true
        );
        SignedHierarchy.Zone good = hierarchy.AddZone(
            "good.com",
            SignedHierarchy.InsecureAddress,
            true
        );
        hierarchy.Delegate(root, com);
        hierarchy.Delegate(com, gost, unsupportedDsDigest: true);
        hierarchy.Delegate(com, good);
        gost.AddCname("alias.gost.com", "www.good.com");
        good.AddA("www.good.com", "192.0.2.20");
        hierarchy.Finish();
        Environment environment = new(hierarchy: hierarchy);

        (IterativeResolutionResult result, DnssecValidationResult validation) =
            await environment.QueryAsync("alias.gost.com", A);

        // The hop is signed by a zone whose only DS cannot be used (RFC 4035 §5.2), so it is
        // insecure, and the secure final answer cannot lift the result back to Secure.
        Assert.Single(result.CnameChain);
        AssertState(DnssecValidationStateEnum.Insecure, validation);
    }

    [Fact]
    public async Task ValidateAsync_WhenTheSignatureWasStripped_IsBogus()
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

        (_, DnssecValidationResult validation) = await environment.QueryAsync("www.example.com", A);

        AssertState(DnssecValidationStateEnum.Bogus, validation);
    }

    [Fact]
    public async Task ValidateAsync_AForgedUnsignedReferralBelowASignedName_IsBogusNotInsecure()
    {
        Environment environment = new();
        DnsName www = DnsName.Parse("www.example.com");
        DnsName nameServer = DnsName.Parse("ns.www.example.com");
        IPAddress attacker = IPAddress.Parse("192.0.2.66");

        // The example.com server answers with a referral for www.example.com that it never
        // signed; the "delegated" server then serves an unsigned A record. The genuine NSEC of
        // www.example.com (no NS bit) proves the name is not a delegation (RFC 6840 §4.4).
        environment.Hierarchy.Client.AddResponse(
            SignedHierarchy.ExampleAddress,
            www,
            A,
            new DnsMessage
            {
                IsResponse = true,
                AuthorityRecords =
                [
                    new DnsResourceRecord(
                        www,
                        DnsResourceRecordTypeEnum.NS,
                        DnsClassEnum.Internet,
                        3600,
                        DnsRecordDataCodecRegistry.Encode(
                            DnsResourceRecordTypeEnum.NS,
                            new NsRecordData(nameServer)
                        )
                    ),
                ],
                AdditionalRecords =
                [
                    new DnsResourceRecord(
                        nameServer,
                        A,
                        DnsClassEnum.Internet,
                        3600,
                        DnsRecordDataCodecRegistry.Encode(A, new ARecordData(attacker))
                    ),
                ],
            }
        );
        environment.Hierarchy.Client.AddResponse(
            attacker,
            www,
            A,
            new DnsMessage
            {
                IsResponse = true,
                IsAuthoritativeAnswer = true,
                Answers =
                [
                    new DnsResourceRecord(
                        www,
                        A,
                        DnsClassEnum.Internet,
                        300,
                        DnsRecordDataCodecRegistry.Encode(
                            A,
                            new ARecordData(IPAddress.Parse("203.0.113.9"))
                        )
                    ),
                ],
            }
        );

        (IterativeResolutionResult result, DnssecValidationResult validation) =
            await environment.QueryAsync("www.example.com", A);

        Assert.Equal(www, result.ZoneCutsTraversed[^1]);
        AssertState(DnssecValidationStateEnum.Bogus, validation);
    }

    [Fact]
    public async Task ValidateAsync_ANegativeAnswerAfterACname_IsCheckedForTheChainsFinalName()
    {
        Environment environment = new();

        (IterativeResolutionResult result, DnssecValidationResult validation) =
            await environment.QueryAsync("alias.example.com", A);

        // alias.example.com owns a CNAME, so a proof for it would fail; the NSEC records prove
        // that missing.example.com does not exist.
        Assert.Equal(RecursiveResolutionStatusEnum.NxDomain, result.Status);
        Assert.Single(result.CnameChain);
        AssertState(DnssecValidationStateEnum.Secure, validation);
    }

    [Fact]
    public async Task ValidateAsync_ANoDataAnswerInASignedZone_IsSecure()
    {
        Environment environment = new();

        (IterativeResolutionResult result, DnssecValidationResult validation) =
            await environment.QueryAsync("www.example.com", DnsResourceRecordTypeEnum.Aaaa);

        Assert.Equal(RecursiveResolutionStatusEnum.NoData, result.Status);
        AssertState(DnssecValidationStateEnum.Secure, validation);
    }

    [Fact]
    public async Task ValidateAsync_ANegativeAnswerWithTheProofStripped_IsBogus()
    {
        Environment environment = new();
        DnsMessage genuine = environment.Hierarchy.Respond(
            SignedHierarchy.ExampleAddress,
            DnsName.Parse("missing.example.com"),
            A
        );
        genuine.AuthorityRecords.RemoveAll(record => record.Type is DnsResourceRecordTypeEnum.Nsec);
        environment.Hierarchy.Client.AddResponse(
            SignedHierarchy.ExampleAddress,
            DnsName.Parse("missing.example.com"),
            A,
            genuine
        );

        (_, DnssecValidationResult validation) = await environment.QueryAsync(
            "missing.example.com",
            A
        );

        AssertState(DnssecValidationStateEnum.Bogus, validation);
    }

    /// <summary>
    ///     This method builds a wildcard-expanded answer for <c>x.example.com</c>, signed with
    ///     the Labels field of <c>*.example.com</c>, with or without the NSEC proof.
    /// </summary>
    private static IterativeResolutionResult BuildWildcardResult(
        Environment environment,
        bool withProof
    )
    {
        DnsName x = DnsName.Parse("x.example.com");
        SignedHierarchy.Zone example = environment.Hierarchy.ZoneByName("example.com");
        DnsResourceRecord answer = new(
            x,
            A,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.Encode(A, new ARecordData(IPAddress.Parse("192.0.2.30")))
        );
        DateTimeOffset now = environment.Clock.GetUtcNow();
        DnsResourceRecord signature = environment.Hierarchy.Fixture.Sign(
            example.Key,
            example.Apex,
            [answer],
            now.AddDays(-1),
            now.AddDays(30),
            labels: 2
        );
        List<DnsResourceRecord> dnssecRecords = [signature];

        if (withProof)
            dnssecRecords.AddRange(
                environment
                    .Hierarchy.Respond(
                        SignedHierarchy.ExampleAddress,
                        x,
                        DnsResourceRecordTypeEnum.Txt
                    )
                    .AuthorityRecords.Where(record =>
                        record.Type is DnsResourceRecordTypeEnum.Nsec
                        || (
                            record.Type == DnsResourceRecordTypeEnum.RrSig
                            && (
                                (RrSigRecordData)
                                    DnsRecordDataCodecRegistry.Decode(
                                        DnsResourceRecordTypeEnum.RrSig,
                                        record.Data
                                    )
                            ).TypeCovered == DnsResourceRecordTypeEnum.Nsec
                        )
                    )
            );

        return new IterativeResolutionResult(
            RecursiveResolutionStatusEnum.Success,
            [answer],
            dnssecRecords,
            null,
            [example.Apex]
        );
    }

    [Fact]
    public async Task ValidateAsync_AWildcardAnswerWithItsNsecProof_IsSecure()
    {
        Environment environment = new();

        DnssecValidationResult validation = await environment.Validator.ValidateAsync(
            DnsName.Parse("x.example.com"),
            A,
            DnsClassEnum.Internet,
            BuildWildcardResult(environment, withProof: true),
            new QueryBudget(environment.Options)
        );

        AssertState(DnssecValidationStateEnum.Secure, validation);
    }

    [Fact]
    public async Task ValidateAsync_AWildcardAnswerWithoutTheProof_IsBogus()
    {
        Environment environment = new();

        DnssecValidationResult validation = await environment.Validator.ValidateAsync(
            DnsName.Parse("x.example.com"),
            A,
            DnsClassEnum.Internet,
            BuildWildcardResult(environment, withProof: false),
            new QueryBudget(environment.Options)
        );

        AssertState(DnssecValidationStateEnum.Bogus, validation);
    }

    [Fact]
    public async Task ValidateAsync_WhenTheDsLookupFailsUnderATrustAnchor_IsBogus()
    {
        Environment environment = new();
        environment.Hierarchy.Client.AddResponse(
            SignedHierarchy.ComAddress,
            DnsName.Parse("example.com"),
            DnsResourceRecordTypeEnum.DS,
            new DnsMessage { IsResponse = true, ResponseCode = DnsResponseCodeEnum.ServerFailure }
        );

        (_, DnssecValidationResult validation) = await environment.QueryAsync("www.example.com", A);

        AssertState(DnssecValidationStateEnum.Bogus, validation);
    }

    [Fact]
    public async Task ValidateAsync_WhenTheRootDnsKeyLookupFailsUnderATrustAnchor_IsBogus()
    {
        Environment environment = new();
        environment.Hierarchy.Client.AddResponse(
            SignedHierarchy.RootAddress,
            DnsName.Root,
            DnsResourceRecordTypeEnum.DnsKey,
            new DnsMessage { IsResponse = true, ResponseCode = DnsResponseCodeEnum.ServerFailure }
        );

        (_, DnssecValidationResult validation) = await environment.QueryAsync("www.example.com", A);

        AssertState(DnssecValidationStateEnum.Bogus, validation);
    }

    [Fact]
    public async Task ValidateAsync_WithNoTrustAnchor_IsIndeterminate()
    {
        Environment environment = new(anchorStore: new FakeTrustAnchorStore([]));

        (_, DnssecValidationResult validation) = await environment.QueryAsync("www.example.com", A);

        AssertState(DnssecValidationStateEnum.Indeterminate, validation);
    }

    [Fact]
    public async Task ValidateAsync_WhenTheRootKeyMatchesNoAnchor_IsBogus()
    {
        DnssecTestFixture other = new();
        Environment environment = new(
            anchorStore: new FakeTrustAnchorStore([
                other.BuildTrustAnchor(DnsName.Root, other.GenerateZoneKey(DnsName.Root)),
            ])
        );

        (_, DnssecValidationResult validation) = await environment.QueryAsync("www.example.com", A);

        AssertState(DnssecValidationStateEnum.Bogus, validation);
    }

    [Fact]
    public async Task TrustCache_Entries_ExpireAtTheEarliestSignatureExpiration()
    {
        Environment environment = new(exampleSignatureLifetime: TimeSpan.FromMinutes(10));
        await environment.QueryAsync("www.example.com", A);
        IDnsCache trustCache = environment.TrustCache;
        Assert.Equal(3, trustCache.GetStatistics().EntryCount);

        // Only example.com's keys are signed for ten minutes; the root and com stay valid.
        environment.Clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal(0, trustCache.EvictExpiredAndOverCapacity());
        environment.Clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(1, trustCache.EvictExpiredAndOverCapacity());
        Assert.Equal(2, trustCache.GetStatistics().EntryCount);
    }

    [Fact]
    public async Task TrustCache_AfterAFlushThroughIDnsCache_TheChainIsVerifiedAgain()
    {
        Environment environment = new();
        await environment.QueryAsync("www.example.com", A);
        IDnsCache trustCache = environment.TrustCache;
        Assert.Equal("dnssec", trustCache.Name);

        int removed = trustCache.Flush();
        (_, DnssecValidationResult validation) = await environment.QueryAsync("www.example.com", A);

        Assert.Equal(3, removed);
        Assert.True(validation.SignatureChecksUsed > 1);
    }

    [Fact]
    public async Task TrustCache_RemovingOneName_ForgetsOnlyThatZone()
    {
        Environment environment = new();
        await environment.QueryAsync("www.example.com", A);
        IDnsCache trustCache = environment.TrustCache;

        Assert.Equal(1, trustCache.Remove(DnsName.Parse("example.com")));
        (_, DnssecValidationResult validation) = await environment.QueryAsync("www.example.com", A);

        // The DS and DNSKEY of example.com are checked again; the root and com are not.
        Assert.Equal(3, validation.SignatureChecksUsed);
    }

    [Fact]
    public async Task TrustCache_WhenTheTrustAnchorsChange_IsEmptied()
    {
        MutableTrustAnchorStore store = new();
        Environment environment = new(
            hierarchy: SignedHierarchy.CreateStandard(
                new ManualTimeProvider(DateTimeOffset.UtcNow)
            ),
            anchorStore: store
        );
        store.Anchors = environment.Hierarchy.Anchors;
        await environment.QueryAsync("www.example.com", A);

        // A second anchor for an unrelated key: the root still validates, but the set differs, so
        // nothing validated under the old set may be reused.
        DnssecTestFixture other = new();
        store.Anchors =
        [
            .. environment.Hierarchy.Anchors,
            other.BuildTrustAnchor(DnsName.Root, other.GenerateZoneKey(DnsName.Root)),
        ];
        (_, DnssecValidationResult validation) = await environment.QueryAsync("www.example.com", A);

        AssertState(DnssecValidationStateEnum.Secure, validation);
        Assert.True(validation.SignatureChecksUsed > 1);
    }

    /// <summary>
    ///     This class is a trust anchor store a test can change between validations.
    /// </summary>
    private sealed class MutableTrustAnchorStore : ITrustAnchorStore
    {
        public IReadOnlyList<TrustAnchor> Anchors { get; set; } = [];

        public IReadOnlyList<TrustAnchor> GetValidAnchors() => Anchors;

        public IReadOnlyList<PersistedTrustAnchor> GetAll() => [];

        public void Save(IReadOnlyList<PersistedTrustAnchor> anchors) { }
    }
}
