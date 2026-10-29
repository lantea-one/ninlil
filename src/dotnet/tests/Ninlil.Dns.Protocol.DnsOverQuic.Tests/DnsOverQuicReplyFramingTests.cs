using System.Buffers.Binary;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.DnsOverQuic.Tests.Fakes;
using Ninlil.Dns.Server.Core.Certificates;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverQuic.Tests;

/// <summary>
///     This class tests that <see cref="DnsOverQuicListener" /> frames each reply exactly once and
///     keeps every reply on its own stream. Forty queries with names of different lengths run at
///     once on one loopback QUIC connection, one stream each; the test reads every reply by its
///     length prefix, checks that the prefix matches the message and that the stream ends right
///     after it, and that the reply answers the stream's own question. Like the other QUIC tests it
///     returns early, reported as passed, when QUIC is not supported on the host.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
public sealed class DnsOverQuicReplyFramingTests
{
    private const int Streams = 40;

    [Fact]
    public async Task ManyConcurrentStreams_EachGetOneExactlyFramedReplyToTheirOwnQuestion()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
            return;

        string statePath = Path.Combine(Path.GetTempPath(), $"doq-framing-{Guid.NewGuid():N}.pfx");
        int port = GetFreeUdpPort();
        DnsQueryProcessingPipeline pipeline = new(
            new AnsweringDispatcher(),
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions()
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance
        );
        DnsOverQuicListener listener = new(
            pipeline,
            new ServiceCollection()
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<DnsOverQuicOptions>(
                new DnsOverQuicOptions
                {
                    Enable = true,
                    Port = port,
                    Certificate = new TlsCertificateOptions
                    {
                        SelfSignedCertificateStatePath = statePath,
                        SelfSignedSubjectAlternativeNames = ["localhost"],
                    },
                }
            ),
            NullLogger<DnsOverQuicListener>.Instance
        );

        try
        {
            await pipeline.StartAsync(CancellationToken.None);
            await listener.StartAsync(CancellationToken.None);

            // The listener binds on a background task after StartAsync returns.
            for (int attempt = 0; attempt < 500 && !listener.IsListening; attempt++)
                await Task.Delay(10);

            Assert.True(listener.IsListening);

            await using QuicConnection connection = await QuicConnection.ConnectAsync(
                new QuicClientConnectionOptions
                {
                    KeepAliveInterval = Timeout.InfiniteTimeSpan,
                    RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, port),
                    ClientAuthenticationOptions = new SslClientAuthenticationOptions
                    {
                        ApplicationProtocols = [new SslApplicationProtocol("doq")],
                        TargetHost = "localhost",
                        RemoteCertificateValidationCallback = (_, _, _, _) => true,
                    },
                    DefaultCloseErrorCode = 0,
                    DefaultStreamErrorCode = 0,
                }
            );

            Task[] checks = Enumerable
                .Range(0, Streams)
                .Select(index => CheckOneStreamAsync(connection, index))
                .ToArray();
            await Task.WhenAll(checks);
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
            await pipeline.StopAsync(CancellationToken.None);
            await pipeline.DisposeAsync();

            if (File.Exists(statePath))
                File.Delete(statePath);
        }
    }

    /// <summary>
    ///     This method sends the query of stream <paramref name="index" /> and checks the framing
    ///     and the content of the reply.
    /// </summary>
    private static async Task CheckOneStreamAsync(QuicConnection connection, int index)
    {
        string name = $"{new string('a', 1 + index)}.example.";
        byte[] query = DnsMessageWriter.Write(
            new DnsMessage
            {
                Id = 0,
                RecursionDesired = true,
                Questions =
                {
                    new DnsQuestion(
                        DnsName.Parse(name),
                        DnsResourceRecordTypeEnum.A,
                        DnsClassEnum.Internet
                    ),
                },
            }
        );
        byte[] framed = new byte[2 + query.Length];
        BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)query.Length);
        query.CopyTo(framed, 2);

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await using QuicStream stream = await connection.OpenOutboundStreamAsync(
            QuicStreamType.Bidirectional,
            timeout.Token
        );
        await stream.WriteAsync(framed, completeWrites: true, timeout.Token);

        byte[] prefix = new byte[2];
        await stream.ReadExactlyAsync(prefix, timeout.Token);
        byte[] reply = new byte[BinaryPrimitives.ReadUInt16BigEndian(prefix)];
        await stream.ReadExactlyAsync(reply, timeout.Token);

        // The prefix counted exactly the message: nothing follows it on the stream.
        Assert.Equal(0, await stream.ReadAsync(new byte[1], timeout.Token));

        DnsMessage response = DnsMessageReader.Read(reply);
        Assert.Equal(name, response.Questions[0].Name.ToString());
        Assert.Single(response.Answers);
    }

    private static int GetFreeUdpPort()
    {
        using System.Net.Sockets.UdpClient probe = new(0);
        return ((IPEndPoint)probe.Client.LocalEndPoint).Port;
    }

    /// <summary>
    ///     This dispatcher answers every query with one A record that repeats the question;
    ///     resolution is not under test here.
    /// </summary>
    private sealed class AnsweringDispatcher : IDnsQueryDispatcher
    {
        public ValueTask<DnsMessage> DispatchAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            DnsQuestion question = context.Request.Questions[0];
            DnsMessage response = new()
            {
                Id = context.Request.Id,
                IsResponse = true,
                RecursionDesired = context.Request.RecursionDesired,
                ResponseCode = DnsResponseCodeEnum.NoError,
                Questions = { question },
            };
            response.Answers.Add(
                new DnsResourceRecord(
                    question.Name,
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet,
                    60,
                    IPAddress.Parse("203.0.113.9").GetAddressBytes()
                )
            );
            return ValueTask.FromResult(response);
        }
    }
}
