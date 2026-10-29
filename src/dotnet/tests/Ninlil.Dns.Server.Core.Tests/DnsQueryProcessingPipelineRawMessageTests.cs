using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Core.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests;

/// <summary>
///     This class tests how <see cref="DnsQueryProcessingPipeline" /> fills
///     <see cref="DnsQueryContext.RawMessage" />: a request that is a whole array is passed on
///     as that array, a slice of a larger array is copied, and TSIG verification sees the bytes
///     as received. The pipeline runs in memory with a capturing resolver and a capturing
///     signature service; no socket is involved, and each test awaits the reply, never a delay.
/// </summary>
public sealed class DnsQueryProcessingPipelineRawMessageTests
{
    private static readonly IPAddress RemoteAddress = IPAddress.Parse("203.0.113.79");

    /// <summary>
    ///     This fake resolver keeps the context of the last query it was given and answers with
    ///     an empty NOERROR reply.
    /// </summary>
    private sealed class CapturingResolver : IDnsQueryResolver
    {
        public DnsQueryContext Context { get; private set; }

        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            Context = context;
            return ValueTask.FromResult(
                DnsQueryResolution.Handled(
                    new DnsMessage
                    {
                        Id = context.Request.Id,
                        IsResponse = true,
                        ResponseCode = DnsResponseCodeEnum.NoError,
                        Questions = [.. context.Request.Questions],
                    }
                )
            );
        }
    }

    /// <summary>
    ///     This fake signature service verifies every request and keeps the raw bytes and TSIG
    ///     offset the pipeline put on the context it verified.
    /// </summary>
    private sealed class CapturingSignatureService : IDnsTransactionSignatureService
    {
        public byte[] RawMessage { get; private set; }

        public int TsigRecordStartOffset { get; private set; }

        public ValueTask<DnsTransactionSignature> VerifyAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            RawMessage = context.RawMessage;
            TsigRecordStartOffset = context.TsigRecordStartOffset;
            return ValueTask.FromResult(
                new DnsTransactionSignature(
                    context.Request.Tsig.KeyName.ToString(),
                    true,
                    DnsResponseCodeEnum.NoError,
                    context.Request.Tsig
                )
            );
        }

        public ValueTask<DnsMessage> SignAsync(
            DnsMessage response,
            DnsTransactionSignature signature,
            CancellationToken stoppingToken = default
        ) => ValueTask.FromResult(response);

        public int GetSignatureLength(DnsTransactionSignature signature) => 0;
    }

    /// <summary>
    ///     This fake publisher drops every event; these tests do not look at events.
    /// </summary>
    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }

    private static DnsMessage BuildQuery(bool signed = false) =>
        new()
        {
            Id = 7,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("raw.example."),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
            Tsig = signed
                ? new DnsTsigOptions(
                    DnsName.Parse("key.example."),
                    DnsTsigOptions.HmacSha256AlgorithmName,
                    1,
                    300,
                    new byte[] { 1, 2, 3, 4 },
                    7,
                    DnsResponseCodeEnum.NoError,
                    []
                )
                : null,
        };

    /// <summary>
    ///     This method runs one query through a pipeline over <paramref name="resolver" /> and
    ///     returns once the reply arrives.
    /// </summary>
    private static async Task RunAsync(
        IDnsQueryResolver resolver,
        ReadOnlyMemory<byte> requestBytes,
        IDnsTransactionSignatureService signatureService = null
    )
    {
        TaskCompletionSource replied = new();
        DnsQueryProcessingPipeline pipeline = new(
            new DnsQueryDispatcher([resolver], new NoOpEventPublisher()),
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions()
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance,
            transactionSignatureService: signatureService
        );
        await pipeline.StartAsync(CancellationToken.None);

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                requestBytes,
                DnsTransportProtocolEnum.Tcp,
                RemoteAddress,
                (_, _) =>
                {
                    replied.TrySetResult();
                    return ValueTask.CompletedTask;
                }
            )
        );

        await replied.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task RawMessage_OfAWholeArrayRequest_IsThatArray()
    {
        byte[] request = DnsMessageWriter.Write(BuildQuery());
        CapturingResolver resolver = new();

        await RunAsync(resolver, request);

        Assert.Same(request, resolver.Context.RawMessage);
    }

    [Fact]
    public async Task RawMessage_OfASliceOfALargerArray_IsACopyOfTheSlice()
    {
        byte[] message = DnsMessageWriter.Write(BuildQuery());
        byte[] framed = new byte[message.Length + 5];
        message.CopyTo(framed, 2);
        CapturingResolver resolver = new();

        await RunAsync(resolver, new ReadOnlyMemory<byte>(framed, 2, message.Length));

        Assert.NotSame(framed, resolver.Context.RawMessage);
        Assert.Equal(message, resolver.Context.RawMessage);
    }

    [Fact]
    public async Task RawMessage_OfAShortPrefixOfAnArray_IsACopyOfThePrefix()
    {
        byte[] message = DnsMessageWriter.Write(BuildQuery());
        byte[] padded = new byte[message.Length + 5];
        message.CopyTo(padded, 0);
        CapturingResolver resolver = new();

        await RunAsync(resolver, new ReadOnlyMemory<byte>(padded, 0, message.Length));

        Assert.NotSame(padded, resolver.Context.RawMessage);
        Assert.Equal(message, resolver.Context.RawMessage);
    }

    [Fact]
    public async Task TsigVerification_SeesTheBytesAsReceivedAndTheRecordOffset()
    {
        byte[] request = DnsMessageWriter.Write(BuildQuery(signed: true));
        CapturingSignatureService signatureService = new();

        await RunAsync(new CapturingResolver(), request, signatureService);

        Assert.Same(request, signatureService.RawMessage);
        DnsMessageReader.Read(request, out int expectedOffset);
        Assert.True(expectedOffset > 0);
        Assert.Equal(expectedOffset, signatureService.TsigRecordStartOffset);
    }
}
