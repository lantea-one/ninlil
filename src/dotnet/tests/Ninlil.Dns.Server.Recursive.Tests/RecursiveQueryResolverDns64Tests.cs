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
///     the A lookup can validate differently.
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
                            new ARecordData(IPAddress.Parse("192.0.2.1"))
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
}
