using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Filtering;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Core.Rewrites;
using Ninlil.Dns.Server.Core.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests;

/// <summary>
///     This class tests that <see cref="DnsQueryProcessingPipeline" /> completes a rewrite's CNAME
///     answer for an A or AAAA question with the target's records (<c>RewriteTargetChaser</c>).
///     It runs the pipeline with a fake rewrite engine, a recording fake resolver behind the real
///     <see cref="DnsQueryDispatcher" />, an optional fake filter engine and a recording event
///     publisher, and reads the reply bytes; no socket is involved.
/// </summary>
public sealed class DnsQueryProcessingPipelineRewriteChaseTests
{
    private static readonly IPAddress RemoteAddress = IPAddress.Parse("203.0.113.79");

    private static readonly DnsName AliasName = DnsName.Parse("www.chase.test.");

    private static readonly DnsName TargetName = DnsName.Parse("target.chase.test.");

    /// <summary>
    ///     This fake rewrite engine answers every A, AAAA and CNAME question for
    ///     <c>www.chase.test</c> with the CNAME record alone and RA clear, as the real engine
    ///     builds a CNAME rewrite. Any other name is not rewritten.
    /// </summary>
    private sealed class CnameRewriteEngine : IDnsRewriteEngine
    {
        public ValueTask<DnsMessage> TryBuildResponseAsync(
            DnsMessage request,
            IPAddress remoteAddress = null,
            CancellationToken stoppingToken = default
        )
        {
            if (request.Questions[0].Name != AliasName)
                return ValueTask.FromResult<DnsMessage>(null);

            DnsMessage response = new()
            {
                Id = request.Id,
                IsResponse = true,
                Opcode = request.Opcode,
                RecursionDesired = request.RecursionDesired,
                RecursionAvailable = false,
                ResponseCode = DnsResponseCodeEnum.NoError,
                Questions = [.. request.Questions],
            };
            response.Answers.Add(
                new DnsResourceRecord(
                    AliasName,
                    DnsResourceRecordTypeEnum.Cname,
                    DnsClassEnum.Internet,
                    300,
                    DnsRecordDataCodecRegistry.Encode(
                        DnsResourceRecordTypeEnum.Cname,
                        new CnameRecordData(TargetName)
                    )
                )
            );
            return ValueTask.FromResult(response);
        }
    }

    /// <summary>
    ///     This fake resolver records every context it is offered and answers with the delegate's
    ///     resolution, so a test chooses the target's answer.
    /// </summary>
    private sealed class RecordingResolver(Func<DnsQueryContext, DnsQueryResolution> answer)
        : IDnsQueryResolver
    {
        public List<DnsQueryContext> Seen { get; } = [];

        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            lock (Seen)
                Seen.Add(context);
            return ValueTask.FromResult(answer(context));
        }
    }

    /// <summary>
    ///     This fake filter engine allows every client and name and denies a response that holds
    ///     the address 192.0.2.7, which stands for a blocked CNAME target in the merged answer.
    /// </summary>
    private sealed class BlockingResponseFilterEngine : IDnsFilterEngine
    {
        public ValueTask<DnsFilterDecision> EvaluateClientAsync(
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default,
            string clientId = null
        ) => ValueTask.FromResult(DnsFilterDecision.Allowed);

        public ValueTask<DnsFilterDecision> EvaluateDomainAsync(
            DnsName queriedName,
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default,
            string clientId = null
        ) => ValueTask.FromResult(DnsFilterDecision.Allowed);

        public ValueTask<DnsFilterDecision> EvaluateResponseAsync(
            DnsMessage response,
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default,
            string clientId = null
        ) =>
            ValueTask.FromResult(
                response.Answers.Any(record => record.Data.AsSpan().SequenceEqual(AddressBytes))
                    ? DnsFilterDecision.Denied("blocked target")
                    : DnsFilterDecision.Allowed
            );

        public DnsMessage BuildDeniedResponse(DnsMessage request) =>
            new()
            {
                Id = request.Id,
                IsResponse = true,
                Opcode = request.Opcode,
                RecursionDesired = request.RecursionDesired,
                ResponseCode = DnsResponseCodeEnum.NxDomain,
                Questions = [.. request.Questions],
            };
    }

    private static readonly byte[] AddressBytes = [192, 0, 2, 7];

    private static DnsMessage BuildQuery(DnsResourceRecordTypeEnum type) =>
        new()
        {
            Id = 77,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            CheckingDisabled = true,
            Questions = { new DnsQuestion(AliasName, type, DnsClassEnum.Internet) },
        };

    /// <summary>
    ///     This method builds the target's reply: a NOERROR answer with one record of
    ///     <paramref name="type" />, RA set.
    /// </summary>
    private static DnsQueryResolution TargetAnswer(
        DnsQueryContext context,
        DnsResourceRecordTypeEnum type
    )
    {
        DnsMessage response = new()
        {
            Id = context.Request.Id,
            IsResponse = true,
            Opcode = context.Request.Opcode,
            RecursionDesired = context.Request.RecursionDesired,
            RecursionAvailable = true,
            ResponseCode = DnsResponseCodeEnum.NoError,
            Questions = [.. context.Request.Questions],
        };
        response.Answers.Add(
            new DnsResourceRecord(
                TargetName,
                type,
                DnsClassEnum.Internet,
                60,
                type == DnsResourceRecordTypeEnum.A
                    ? AddressBytes
                    : IPAddress.Parse("2001:db8::7").GetAddressBytes()
            )
        );
        return DnsQueryResolution.Handled(response);
    }

    private static async Task<DnsMessage> RunAsync(
        RecordingResolver resolver,
        DnsMessage query,
        RecordingEventPublisher publisher,
        IDnsFilterEngine filterEngine = null
    )
    {
        DnsQueryProcessingPipeline pipeline = new(
            new DnsQueryDispatcher([resolver], publisher),
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions()
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance,
            filterEngine: filterEngine,
            rewriteEngine: new CnameRewriteEngine(),
            eventPublisher: publisher
        );
        await pipeline.StartAsync(CancellationToken.None);

        TaskCompletionSource<byte[]> received = new();
        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                DnsMessageWriter.Write(query),
                DnsTransportProtocolEnum.Tcp,
                RemoteAddress,
                (bytes, _) =>
                {
                    received.TrySetResult(bytes.ToArray());
                    return ValueTask.CompletedTask;
                }
            )
        );
        DnsMessage reply = DnsMessageReader.Read(
            (await received.Task.WaitAsync(TimeSpan.FromSeconds(5))).AsSpan(),
            out _
        );

        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
        return reply;
    }

    [Fact]
    public async Task AQuery_ForACnameRewrite_AnswersTheCnameAndTheTargetsAddress()
    {
        RecordingResolver resolver = new(c => TargetAnswer(c, DnsResourceRecordTypeEnum.A));

        DnsMessage reply = await RunAsync(
            resolver,
            BuildQuery(DnsResourceRecordTypeEnum.A),
            new RecordingEventPublisher()
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, reply.ResponseCode);
        Assert.Equal(
            [DnsResourceRecordTypeEnum.Cname, DnsResourceRecordTypeEnum.A],
            reply.Answers.Select(record => record.Type)
        );
        Assert.Equal(AliasName, reply.Answers[0].Name);
        Assert.Equal(TargetName, reply.Answers[1].Name);
        Assert.True(reply.RecursionAvailable);
        Assert.False(reply.IsAuthoritativeAnswer);
        Assert.False(reply.AuthenticData);
        Assert.Equal(77, reply.Id);
        Assert.Equal(AliasName, reply.Questions[0].Name);
    }

    [Fact]
    public async Task AaaaQuery_ForACnameRewrite_AnswersTheCnameAndTheTargetsAddress()
    {
        RecordingResolver resolver = new(c => TargetAnswer(c, DnsResourceRecordTypeEnum.Aaaa));

        DnsMessage reply = await RunAsync(
            resolver,
            BuildQuery(DnsResourceRecordTypeEnum.Aaaa),
            new RecordingEventPublisher()
        );

        Assert.Equal(
            [DnsResourceRecordTypeEnum.Cname, DnsResourceRecordTypeEnum.Aaaa],
            reply.Answers.Select(record => record.Type)
        );
    }

    [Fact]
    public async Task AQuery_WhenTheTargetDoesNotExist_AnswersNxDomainWithTheCnameAndTheSoa()
    {
        DnsResourceRecord soa = new(
            DnsName.Parse("chase.test."),
            DnsResourceRecordTypeEnum.Soa,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Soa,
                new SoaRecordData(
                    DnsName.Parse("ns1.chase.test."),
                    DnsName.Parse("admin.chase.test."),
                    1,
                    3600,
                    600,
                    604800,
                    300
                )
            )
        );
        RecordingResolver resolver = new(c =>
        {
            DnsMessage response = new()
            {
                Id = c.Request.Id,
                IsResponse = true,
                RecursionAvailable = true,
                ResponseCode = DnsResponseCodeEnum.NxDomain,
                Questions = [.. c.Request.Questions],
            };
            response.AuthorityRecords.Add(soa);
            return DnsQueryResolution.Handled(response);
        });

        DnsMessage reply = await RunAsync(
            resolver,
            BuildQuery(DnsResourceRecordTypeEnum.A),
            new RecordingEventPublisher()
        );

        Assert.Equal(DnsResponseCodeEnum.NxDomain, reply.ResponseCode);
        Assert.Single(reply.Answers);
        Assert.Equal(DnsResourceRecordTypeEnum.Cname, reply.Answers[0].Type);
        Assert.Single(reply.AuthorityRecords);
        Assert.Equal(DnsResourceRecordTypeEnum.Soa, reply.AuthorityRecords[0].Type);
    }

    [Fact]
    public async Task AQuery_WhenNoResolverClaimsTheTarget_AnswersTheCnameAloneWithRaClear()
    {
        RecordingResolver resolver = new(_ => DnsQueryResolution.NotHandled);

        DnsMessage reply = await RunAsync(
            resolver,
            BuildQuery(DnsResourceRecordTypeEnum.A),
            new RecordingEventPublisher()
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, reply.ResponseCode);
        Assert.Single(reply.Answers);
        Assert.Equal(DnsResourceRecordTypeEnum.Cname, reply.Answers[0].Type);
        Assert.False(reply.RecursionAvailable);
    }

    [Fact]
    public async Task AQuery_WhenTheTargetLookupFailsWithServfail_AnswersTheCnameAloneWithRaClear()
    {
        RecordingResolver resolver = new(c =>
            DnsQueryResolution.Handled(
                new DnsMessage
                {
                    Id = c.Request.Id,
                    IsResponse = true,
                    RecursionAvailable = true,
                    ResponseCode = DnsResponseCodeEnum.ServerFailure,
                    Questions = [.. c.Request.Questions],
                }
            )
        );

        DnsMessage reply = await RunAsync(
            resolver,
            BuildQuery(DnsResourceRecordTypeEnum.A),
            new RecordingEventPublisher()
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, reply.ResponseCode);
        Assert.Single(reply.Answers);
        Assert.False(reply.RecursionAvailable);
    }

    [Fact]
    public async Task CnameQuery_ForACnameRewrite_AnswersTheCnameAloneAndAsksNoResolver()
    {
        RecordingResolver resolver = new(c => TargetAnswer(c, DnsResourceRecordTypeEnum.A));

        DnsMessage reply = await RunAsync(
            resolver,
            BuildQuery(DnsResourceRecordTypeEnum.Cname),
            new RecordingEventPublisher()
        );

        Assert.Single(reply.Answers);
        Assert.Equal(DnsResourceRecordTypeEnum.Cname, reply.Answers[0].Type);
        Assert.Empty(resolver.Seen);
    }

    [Fact]
    public async Task TheLookup_IsInternalCarriesTheTargetAndTheClientsFlags_AndIsLoggedOnce()
    {
        RecordingResolver resolver = new(c => TargetAnswer(c, DnsResourceRecordTypeEnum.A));
        RecordingEventPublisher publisher = new();

        _ = await RunAsync(resolver, BuildQuery(DnsResourceRecordTypeEnum.A), publisher);

        DnsQueryContext seen = Assert.Single(resolver.Seen);
        Assert.True(seen.IsInternalLookup);
        Assert.Equal(TargetName, seen.Request.Questions[0].Name);
        Assert.Equal(DnsResourceRecordTypeEnum.A, seen.Request.Questions[0].Type);
        Assert.True(seen.Request.RecursionDesired);
        Assert.True(seen.Request.CheckingDisabled);
        Assert.Equal(RemoteAddress, seen.RemoteAddress);

        // The query log and live feed see the client's query once, with both records.
        Assert.Empty(publisher.Published.OfType<DnsQueryReceivedEvent>());
        DnsQueryAnsweredEvent answered = Assert.Single(
            publisher.Published.OfType<DnsQueryAnsweredEvent>()
        );
        Assert.Equal("www.chase.test.", answered.QueriedName);
        Assert.Equal(2, answered.AnswerCount);
    }

    [Fact]
    public async Task AQuery_WhenTheFilterBlocksTheMergedAnswer_AnswersTheDenial()
    {
        RecordingResolver resolver = new(c => TargetAnswer(c, DnsResourceRecordTypeEnum.A));

        DnsMessage reply = await RunAsync(
            resolver,
            BuildQuery(DnsResourceRecordTypeEnum.A),
            new RecordingEventPublisher(),
            new BlockingResponseFilterEngine()
        );

        Assert.Equal(DnsResponseCodeEnum.NxDomain, reply.ResponseCode);
        Assert.Empty(reply.Answers);
    }
}
