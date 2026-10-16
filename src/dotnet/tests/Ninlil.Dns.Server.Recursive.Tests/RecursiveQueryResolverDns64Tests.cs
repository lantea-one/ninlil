using System.Net;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Core.Dns64;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Tests.Fakes;
using Ninlil.Dns.Server.Recursive.Validation;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests how <see cref="RecursiveQueryResolver" /> validates the A answer behind a
///     DNS64 synthesis (RFC 6147 §5.5). It uses a fake engine with canned AAAA (NODATA) and A
///     results and a validator whose outcome depends on the queried type, so the AAAA lookup and
///     the A lookup can validate differently. It also covers the exclusion of IPv4-mapped AAAA
///     records (RFC 6147 §5.1.4) with canned AAAA answers.
/// </summary>
public sealed class RecursiveQueryResolverDns64Tests
{
    private static readonly DnsName QueriedName = DnsName.Parse("example.com");

    /// <summary>
    ///     This class is a validator that returns one result for A queries and another for the
    ///     rest.
    /// </summary>
    private sealed class PerTypeValidator(
        DnssecValidationResult aResult,
        DnssecValidationResult otherResult
    ) : IDnssecValidator
    {
        public Task<DnssecValidationResult> ValidateAsync(
            DnsName queriedName,
            DnsResourceRecordTypeEnum queriedType,
            DnsClassEnum recordClass,
            IterativeResolutionResult iterativeResult,
            QueryBudget budget,
            CancellationToken stoppingToken = default,
            IDnssecRecordFetcher fetcher = null
        ) => Task.FromResult(queriedType == DnsResourceRecordTypeEnum.A ? aResult : otherResult);
    }

    private static async Task<DnsMessage> AskAsync(
        DnssecValidationResult aValidation,
        bool checkingDisabled = false,
        bool dnssecOk = false
    )
    {
        FakeIterativeResolutionEngine engine = new();
        engine.AddResult(
            QueriedName,
            DnsResourceRecordTypeEnum.Aaaa,
            new IterativeResolutionResult(RecursiveResolutionStatusEnum.NoData, [], [], null, [])
        );
        engine.AddResult(
            QueriedName,
            DnsResourceRecordTypeEnum.A,
            new IterativeResolutionResult(
                RecursiveResolutionStatusEnum.Success,
                [
                    new DnsResourceRecord(
                        QueriedName,
                        DnsResourceRecordTypeEnum.A,
                        DnsClassEnum.Internet,
                        300,
                        DnsRecordDataCodecRegistry.Encode(
                            DnsResourceRecordTypeEnum.A,
                            new ARecordData(IPAddress.Parse("198.41.0.4"))
                        )
                    ),
                ],
                [],
                null,
                []
            )
        );

        RecursiveQueryResolver resolver = new(
            engine,
            new PerTypeValidator(aValidation, DnssecValidationResult.Secure()),
            new StaticOptionsMonitor<RecursiveServerOptions>(
                new RecursiveServerOptions { EnableRecursion = true }
            ),
            dns64Service: new Dns64SynthesisService(
                new StaticOptionsMonitor<Dns64Options>(new Dns64Options { Enable = true })
            )
        );
        DnsMessage request = new()
        {
            RecursionDesired = true,
            CheckingDisabled = checkingDisabled,
            Questions =
            [
                new DnsQuestion(QueriedName, DnsResourceRecordTypeEnum.Aaaa, DnsClassEnum.Internet),
            ],
        };

        if (dnssecOk)
            request.Edns = new DnsEdnsOptions(0, 1232, true, []);

        DnsQueryResolution resolution = await resolver.TryResolveAsync(
            new DnsQueryContext(
                request,
                DnsTransportProtocolEnum.Udp,
                IPAddress.Parse("203.0.113.5"),
                DateTimeOffset.UtcNow
            )
        );

        Assert.True(resolution.IsHandled);

        return resolution.Response;
    }

    [Fact]
    public async Task TryResolveAsync_ABogusAAnswerWithCdClear_IsServFail()
    {
        DnsMessage response = await AskAsync(DnssecValidationResult.Bogus("test"));

        Assert.Equal(DnsResponseCodeEnum.ServerFailure, response.ResponseCode);
        Assert.Empty(response.Answers);
    }

    [Fact]
    public async Task TryResolveAsync_ABogusAAnswerWithCdSetAndDoClear_IsNoDataWithoutSynthesis()
    {
        DnsMessage response = await AskAsync(
            DnssecValidationResult.Bogus("test"),
            checkingDisabled: true
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Empty(response.Answers);
    }

    [Fact]
    public async Task TryResolveAsync_ADoAndCdClient_GetsNoSynthesisEvenWhenTheAAnswerIsSecure()
    {
        DnsMessage response = await AskAsync(
            DnssecValidationResult.Secure(),
            checkingDisabled: true,
            dnssecOk: true
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Empty(response.Answers);
    }

    [Fact]
    public async Task TryResolveAsync_ASecureAAnswer_IsSynthesizedWithoutAd()
    {
        DnsMessage response = await AskAsync(DnssecValidationResult.Secure(), dnssecOk: true);

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        DnsResourceRecord answer = Assert.Single(response.Answers);
        Assert.Equal(DnsResourceRecordTypeEnum.Aaaa, answer.Type);
        Assert.False(response.AuthenticData);
    }

    [Fact]
    public async Task TryResolveAsync_AnInsecureAAnswer_IsSynthesized()
    {
        DnsMessage response = await AskAsync(DnssecValidationResult.Insecure());

        Assert.Single(response.Answers);
    }

    private static DnsResourceRecord AaaaRecord(string address) =>
        new(
            QueriedName,
            DnsResourceRecordTypeEnum.Aaaa,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Aaaa,
                new AaaaRecordData(IPAddress.Parse(address))
            )
        );

    /// <summary>
    ///     This method builds an RRSIG over the AAAA RRset; the signature bytes are never checked
    ///     because the fake validator decides the outcome.
    /// </summary>
    private static DnsResourceRecord AaaaSignature() =>
        new(
            QueriedName,
            DnsResourceRecordTypeEnum.RrSig,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.RrSig,
                new RrSigRecordData(
                    DnsResourceRecordTypeEnum.Aaaa,
                    DnssecAlgorithmEnum.EcdsaP256Sha256,
                    2,
                    300,
                    2_000_000_000,
                    1_000_000_000,
                    1,
                    DnsName.Parse("example.com"),
                    [1, 2, 3]
                )
            )
        );

    /// <summary>
    ///     This method asks the AAAA question when the engine answers it with
    ///     <paramref name="aaaaAddresses" /> (and its RRSIG) and answers the A question with
    ///     198.41.0.4. The AAAA validation is Secure, so a surviving AD bit would show.
    /// </summary>
    private static async Task<DnsMessage> AskWithAaaaAnswerAsync(
        string[] aaaaAddresses,
        bool checkingDisabled = false
    )
    {
        FakeIterativeResolutionEngine engine = new();
        engine.AddResult(
            QueriedName,
            DnsResourceRecordTypeEnum.Aaaa,
            new IterativeResolutionResult(
                RecursiveResolutionStatusEnum.Success,
                [.. aaaaAddresses.Select(AaaaRecord)],
                [AaaaSignature()],
                null,
                []
            )
        );
        engine.AddResult(
            QueriedName,
            DnsResourceRecordTypeEnum.A,
            new IterativeResolutionResult(
                RecursiveResolutionStatusEnum.Success,
                [
                    new DnsResourceRecord(
                        QueriedName,
                        DnsResourceRecordTypeEnum.A,
                        DnsClassEnum.Internet,
                        300,
                        DnsRecordDataCodecRegistry.Encode(
                            DnsResourceRecordTypeEnum.A,
                            new ARecordData(IPAddress.Parse("198.41.0.4"))
                        )
                    ),
                ],
                [],
                null,
                []
            )
        );

        RecursiveQueryResolver resolver = new(
            engine,
            new PerTypeValidator(DnssecValidationResult.Secure(), DnssecValidationResult.Secure()),
            new StaticOptionsMonitor<RecursiveServerOptions>(
                new RecursiveServerOptions { EnableRecursion = true }
            ),
            dns64Service: new Dns64SynthesisService(
                new StaticOptionsMonitor<Dns64Options>(new Dns64Options { Enable = true })
            )
        );
        DnsMessage request = new()
        {
            RecursionDesired = true,
            CheckingDisabled = checkingDisabled,
            Questions =
            [
                new DnsQuestion(QueriedName, DnsResourceRecordTypeEnum.Aaaa, DnsClassEnum.Internet),
            ],
            Edns = new DnsEdnsOptions(0, 1232, true, []),
        };

        DnsQueryResolution resolution = await resolver.TryResolveAsync(
            new DnsQueryContext(
                request,
                DnsTransportProtocolEnum.Udp,
                IPAddress.Parse("203.0.113.5"),
                DateTimeOffset.UtcNow
            )
        );

        Assert.True(resolution.IsHandled);

        return resolution.Response;
    }

    private static IPAddress AddressOf(DnsResourceRecord record) =>
        ((AaaaRecordData)DnsRecordDataCodecRegistry.Decode(record.Type, record.Data)).Address;

    [Fact]
    public async Task TryResolveAsync_AnAaaaAnswerOfOnlyMappedAddresses_IsSynthesizedFromTheAAnswerWithoutAd()
    {
        DnsMessage response = await AskWithAaaaAnswerAsync(["::ffff:192.0.2.1"]);

        DnsResourceRecord answer = Assert.Single(response.Answers);
        Assert.Equal(IPAddress.Parse("64:ff9b::c629:4"), AddressOf(answer));
        Assert.False(response.AuthenticData);
    }

    [Fact]
    public async Task TryResolveAsync_AnAaaaAnswerWithMappedAndRealAddresses_KeepsOnlyTheRealOnesAndDropsTheSignatureAndAd()
    {
        // RFC 6147 §5.1.4: the default is to build the answer from the AAAA records outside the
        // excluded range; the RRSIG over the original RRset no longer matches and goes too.
        DnsMessage response = await AskWithAaaaAnswerAsync(["::ffff:192.0.2.1", "2001:db8::1"]);

        DnsResourceRecord answer = Assert.Single(response.Answers);
        Assert.Equal(DnsResourceRecordTypeEnum.Aaaa, answer.Type);
        Assert.Equal(IPAddress.Parse("2001:db8::1"), AddressOf(answer));
        Assert.False(response.AuthenticData);
    }

    [Fact]
    public async Task TryResolveAsync_AnAaaaAnswerWithoutMappedAddresses_KeepsSignatureAndAd()
    {
        DnsMessage response = await AskWithAaaaAnswerAsync(["2001:db8::1"]);

        Assert.Equal(2, response.Answers.Count);
        Assert.Contains(response.Answers, record => record.Type == DnsResourceRecordTypeEnum.RrSig);
        Assert.True(response.AuthenticData);
    }

    [Fact]
    public async Task TryResolveAsync_ADoAndCdClient_GetsTheMappedAaaaAnswerUnchanged()
    {
        // RFC 6147 §5.5: DO and CD together mean the client wants the data as it came.
        DnsMessage response = await AskWithAaaaAnswerAsync(
            ["::ffff:192.0.2.1"],
            checkingDisabled: true
        );

        Assert.Equal(2, response.Answers.Count);
        Assert.Equal(IPAddress.Parse("::ffff:192.0.2.1"), AddressOf(response.Answers[0]));
    }
}
