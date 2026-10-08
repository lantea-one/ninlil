using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Cookies;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Core.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests;

/// <summary>
///     This class tests how <see cref="DnsQueryProcessingPipeline" /> uses an
///     <see cref="IDnsTransactionSignatureService" />: verifying a signed request, skipping the
///     resolvers when verification fails, and signing the reply as the last step, after the cookie
///     option and the UDP truncation. The service is a fake, so no key store is involved; the
///     pipeline runs without a socket and the tests capture the reply bytes.
/// </summary>
public sealed class DnsQueryProcessingPipelineTransactionSignatureTests
{
    private static readonly IPAddress RemoteAddress = IPAddress.Parse("203.0.113.78");

    private static readonly byte[] RequestMac = [1, 2, 3, 4];

    /// <summary>
    ///     This fake verifies to a fixed outcome and "signs" by attaching a TSIG record with a
    ///     marker MAC to a copy. It records what <c>SignAsync</c> was given.
    /// </summary>
    private sealed class FakeSignatureService(
        bool verified,
        DnsResponseCodeEnum error = DnsResponseCodeEnum.NoError,
        int signatureLength = 0
    ) : IDnsTransactionSignatureService
    {
        public int VerifyCount { get; private set; }

        public int SignCount { get; private set; }

        public DnsMessage SignedInput { get; private set; }

        public bool InputWasTruncated { get; private set; }

        public bool InputHadCookie { get; private set; }

        public ValueTask<DnsTransactionSignature> VerifyAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            VerifyCount++;
            return ValueTask.FromResult(
                new DnsTransactionSignature(
                    context.Request.Tsig.KeyName.ToString(),
                    verified,
                    error,
                    context.Request.Tsig
                )
            );
        }

        public ValueTask<DnsMessage> SignAsync(
            DnsMessage response,
            DnsTransactionSignature signature,
            CancellationToken stoppingToken = default
        )
        {
            SignCount++;
            SignedInput = response;
            InputWasTruncated = response.IsTruncated;
            InputHadCookie =
                response.Edns?.Options.Any(option =>
                    option.Code == (ushort)EdnsOptionCodeEnum.Cookie
                ) == true;

            DnsMessage copy = new()
            {
                Id = response.Id,
                IsResponse = true,
                Opcode = response.Opcode,
                IsTruncated = response.IsTruncated,
                ResponseCode = response.ResponseCode,
                Questions = [.. response.Questions],
                Answers = [.. response.Answers],
                Edns = response.Edns,
                Tsig = new DnsTsigOptions(
                    signature.RequestTsig.KeyName,
                    signature.RequestTsig.AlgorithmName,
                    1,
                    300,
                    new byte[] { 9, 9, 9 },
                    response.Id,
                    DnsResponseCodeEnum.NoError,
                    []
                ),
            };

            return ValueTask.FromResult(copy);
        }

        public int GetSignatureLength(DnsTransactionSignature signature) => signatureLength;
    }

    private sealed class CountingResolver(int answerCount) : IDnsQueryResolver
    {
        public int CallCount { get; private set; }

        public DnsTransactionSignature SeenSignature { get; private set; }

        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            CallCount++;
            SeenSignature = context.TransactionSignature;

            DnsMessage response = new()
            {
                Id = context.Request.Id,
                IsResponse = true,
                Opcode = context.Request.Opcode,
                Questions = [.. context.Request.Questions],
            };

            for (int i = 0; i < answerCount; i++)
                response.Answers.Add(
                    new DnsResourceRecord(
                        DnsName.Parse("example.com."),
                        DnsResourceRecordTypeEnum.A,
                        DnsClassEnum.Internet,
                        300,
                        new byte[] { 192, 0, 2, (byte)i }
                    )
                );

            return ValueTask.FromResult(DnsQueryResolution.Handled(response));
        }
    }

    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }

    private static DnsMessage BuildQuery(bool signed, DnsEdnsOption cookieOption = null) =>
        new()
        {
            Id = 77,
            Opcode = DnsOpcodeEnum.Query,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("example.com."),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
            Edns = cookieOption is null ? null : new DnsEdnsOptions(0, 4096, false, [cookieOption]),
            Tsig = signed
                ? new DnsTsigOptions(
                    DnsName.Parse("key.example."),
                    DnsTsigOptions.HmacSha256AlgorithmName,
                    1,
                    300,
                    RequestMac,
                    77,
                    DnsResponseCodeEnum.NoError,
                    []
                )
                : null,
        };

    private static async Task<DnsMessage> RunAsync(
        IDnsQueryResolver resolver,
        IDnsTransactionSignatureService signatureService,
        DnsMessage query,
        DnsCookieService cookieService = null
    )
    {
        TaskCompletionSource<byte[]> responseReceived = new();
        DnsQueryProcessingPipeline pipeline = new(
            new DnsQueryDispatcher([resolver], new NoOpEventPublisher()),
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions()
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance,
            cookieService: cookieService,
            transactionSignatureService: signatureService
        );
        await pipeline.StartAsync(CancellationToken.None);

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                DnsMessageWriter.Write(query),
                DnsTransportProtocolEnum.Udp,
                RemoteAddress,
                (bytes, _) =>
                {
                    responseReceived.TrySetResult(bytes.ToArray());
                    return ValueTask.CompletedTask;
                }
            )
        );

        byte[] bytes = await responseReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();

        return DnsMessageReader.Read(bytes, out _);
    }

    [Fact]
    public async Task VerifiedRequest_IsSignedAfterTheCookieOptionIsAdded()
    {
        CountingResolver resolver = new(answerCount: 1);
        FakeSignatureService service = new(verified: true);
        DnsCookieService cookieService = new(
            Options.Create(new DnsCookieOptions { Enable = true, Require = false })
        );
        DnsEdnsOption clientCookie = new(
            (ushort)EdnsOptionCodeEnum.Cookie,
            Enumerable.Range(1, 8).Select(i => (byte)i).ToArray()
        );

        DnsMessage response = await RunAsync(
            resolver,
            service,
            BuildQuery(signed: true, clientCookie),
            cookieService
        );

        Assert.Equal(1, service.VerifyCount);
        Assert.Equal(1, service.SignCount);
        // The MAC covers the cookie option, so it has to be there when the reply is signed.
        Assert.True(service.InputHadCookie);
        Assert.NotNull(response.Tsig);
        Assert.Equal(new byte[] { 9, 9, 9 }, response.Tsig.Mac);
        Assert.True(resolver.SeenSignature is { IsVerified: true });
    }

    [Fact]
    public async Task FailedSignature_SkipsDispatchAndStillSignsTheErrorReply()
    {
        CountingResolver resolver = new(answerCount: 1);
        FakeSignatureService service = new(
            verified: false,
            error: DnsResponseCodeEnum.BadVersionOrBadSignature
        );

        DnsMessage response = await RunAsync(resolver, service, BuildQuery(signed: true));

        Assert.Equal(0, resolver.CallCount);
        Assert.Equal(1, service.SignCount);
        // The 4-bit header carries NOTAUTH; the extended error belongs in the TSIG record.
        Assert.Equal(DnsResponseCodeEnum.NotAuthoritative, service.SignedInput.ResponseCode);
        Assert.Equal(DnsResponseCodeEnum.NotAuthoritative, response.ResponseCode);
        Assert.Empty(response.Answers);
        Assert.NotNull(response.Tsig);
    }

    [Fact]
    public async Task FailedSignatureWithAMalformedMac_IsAnsweredFormatError()
    {
        CountingResolver resolver = new(answerCount: 1);
        FakeSignatureService service = new(verified: false, error: DnsResponseCodeEnum.FormatError);

        await RunAsync(resolver, service, BuildQuery(signed: true));

        Assert.Equal(DnsResponseCodeEnum.FormatError, service.SignedInput.ResponseCode);
    }

    [Fact]
    public async Task UdpAnswerThatFitsOnlyWithoutTheTsigRecord_IsTruncatedThenSigned()
    {
        // About 20 answers of 16 octets each: well inside 512 octets alone, over it once 200
        // octets are reserved for the signature.
        CountingResolver resolver = new(answerCount: 25);
        FakeSignatureService service = new(verified: true, signatureLength: 200);

        DnsMessage response = await RunAsync(resolver, service, BuildQuery(signed: true));

        Assert.True(service.InputWasTruncated);
        Assert.True(response.IsTruncated);
        Assert.Empty(response.Answers);
        Assert.NotNull(response.Tsig);
    }

    [Fact]
    public async Task UdpAnswerThatFitsWithTheTsigRecord_IsNotTruncated()
    {
        CountingResolver resolver = new(answerCount: 25);
        FakeSignatureService service = new(verified: true, signatureLength: 10);

        DnsMessage response = await RunAsync(resolver, service, BuildQuery(signed: true));

        Assert.False(response.IsTruncated);
        Assert.Equal(25, response.Answers.Count);
        Assert.NotNull(response.Tsig);
    }

    [Fact]
    public async Task UnsignedRequest_NeverReachesTheServiceAndIsNotSigned()
    {
        CountingResolver resolver = new(answerCount: 1);
        FakeSignatureService service = new(verified: true);

        DnsMessage response = await RunAsync(resolver, service, BuildQuery(signed: false));

        Assert.Equal(0, service.VerifyCount);
        Assert.Equal(0, service.SignCount);
        Assert.Null(resolver.SeenSignature);
        Assert.Null(response.Tsig);
        Assert.Single(response.Answers);
    }
}
