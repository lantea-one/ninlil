using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.RateLimiting;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Transfer;

/// <summary>
///     This class tests <see cref="ZoneTransferRequestRunner" /> with a fake
///     <see cref="IZoneTransferService" />, a fake limiter and a recording event publisher, so
///     it covers the rate-limit gate and the answered event without sockets or a database.
/// </summary>
public sealed class ZoneTransferRequestRunnerTests
{
    private static readonly IPAddress Client = IPAddress.Parse("192.0.2.7");

    [Fact]
    public async Task ADeniedRateLimit_NeverCallsTheTransferServiceAndWritesNothing()
    {
        FakeTransferService service = new(Message(DnsResponseCodeEnum.NoError, 3));
        RecordingPublisher publisher = new();
        ZoneTransferRequestRunner runner = Build(service, new FakeLimiter(allow: true), publisher);
        ZoneTransferRequestRunner subnetDenied = Build(
            service,
            new FakeLimiter(allow: true, DnsSubnetRateLimitDecisionEnum.Drop),
            publisher
        );
        ZoneTransferRequestRunner ipDenied = Build(
            service,
            new FakeLimiter(allow: false),
            publisher
        );
        List<byte[]> written = [];

        bool ipResult = await Run(ipDenied, written);
        bool subnetResult = await Run(subnetDenied, written);

        Assert.False(ipResult);
        Assert.False(subnetResult);
        Assert.Equal(0, service.Calls);
        Assert.Empty(written);
        Assert.Empty(publisher.Events);

        // Control: the same service runs when the limiter allows.
        Assert.True(await Run(runner, written));
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task ASuccessfulAxfr_PublishesOneEventWithTheTotalRecordCount()
    {
        FakeTransferService service = new(
            Message(DnsResponseCodeEnum.NoError, 3),
            Message(DnsResponseCodeEnum.NoError, 2)
        );
        RecordingPublisher publisher = new();
        List<byte[]> written = [];

        bool served = await Run(Build(service, null, publisher), written);

        Assert.True(served);
        Assert.Equal(2, written.Count);
        DnsQueryAnsweredEvent answered = Assert.Single(publisher.Events);
        Assert.Equal(5, answered.AnswerCount);
        Assert.Equal(DnsResponseCodeEnum.NoError, answered.ResponseCode);
        Assert.Equal(DnsResourceRecordTypeEnum.Axfr, answered.QueriedType);
        Assert.Equal("zone.example", answered.QueriedName.TrimEnd('.'));
        Assert.Equal(DnsTransportProtocolEnum.Tcp, answered.Protocol);
        Assert.Equal(Client, answered.RemoteAddress);
    }

    [Fact]
    public async Task ARefusedTransfer_PublishesAnEventWithTheRefusedCode()
    {
        FakeTransferService service = new(Message(DnsResponseCodeEnum.Refused, 0));
        RecordingPublisher publisher = new();
        List<byte[]> written = [];

        await Run(Build(service, null, publisher), written);

        Assert.Single(written);
        DnsQueryAnsweredEvent answered = Assert.Single(publisher.Events);
        Assert.Equal(DnsResponseCodeEnum.Refused, answered.ResponseCode);
        Assert.Equal(0, answered.AnswerCount);
    }

    [Fact]
    public async Task WithoutALimiterOrPublisher_TheTransferStillRuns()
    {
        FakeTransferService service = new(Message(DnsResponseCodeEnum.NoError, 1));
        List<byte[]> written = [];

        Assert.True(await Run(Build(service, null, null), written));
        Assert.Single(written);
    }

    private static ZoneTransferRequestRunner Build(
        IZoneTransferService service,
        IDnsRateLimiter limiter,
        IDnsServerEventPublisher publisher
    )
    {
        ServiceCollection services = new();
        services.AddSingleton(service);
        return new ZoneTransferRequestRunner(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            limiter,
            publisher
        );
    }

    private static Task<bool> Run(ZoneTransferRequestRunner runner, List<byte[]> written)
    {
        DnsMessage request = new()
        {
            Id = 7,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("zone.example."),
                    DnsResourceRecordTypeEnum.Axfr,
                    DnsClassEnum.Internet
                ),
            },
        };

        return runner.RunAsync(
            request,
            DnsMessageWriter.Write(request),
            0,
            Client,
            DnsTransportProtocolEnum.Tcp,
            DnsResourceRecordTypeEnum.Axfr,
            (bytes, _) =>
            {
                written.Add(bytes);
                return ValueTask.CompletedTask;
            },
            CancellationToken.None
        );
    }

    private static DnsMessage Message(DnsResponseCodeEnum code, int answers)
    {
        DnsMessage message = new()
        {
            Id = 7,
            IsResponse = true,
            ResponseCode = code,
        };

        for (int index = 0; index < answers; index++)
            message.Answers.Add(
                new DnsResourceRecord(
                    DnsName.Parse("zone.example."),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet,
                    60,
                    [192, 0, 2, (byte)index]
                )
            );

        return message;
    }

    private sealed class FakeLimiter(
        bool allow,
        DnsSubnetRateLimitDecisionEnum subnet = DnsSubnetRateLimitDecisionEnum.Allow
    ) : IDnsRateLimiter
    {
        public ValueTask<bool> TryAcquireAsync(
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default
        ) => ValueTask.FromResult(allow);

        public ValueTask<DnsSubnetRateLimitDecisionEnum> CheckSubnetLimitAsync(
            IPAddress remoteAddress,
            bool isUdp,
            CancellationToken stoppingToken = default
        ) => ValueTask.FromResult(subnet);
    }

    private sealed class RecordingPublisher : IDnsServerEventPublisher
    {
        public List<DnsQueryAnsweredEvent> Events { get; } = [];

        public void Publish(IDnsServerEvent domainEvent)
        {
            if (domainEvent is DnsQueryAnsweredEvent answered)
                Events.Add(answered);
        }
    }

    private sealed class FakeTransferService(params DnsMessage[] messages) : IZoneTransferService
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public async IAsyncEnumerable<DnsMessage> CreateAxfrResponseAsync(
            DnsQueryContext context,
            [EnumeratorCancellation] CancellationToken stoppingToken = default
        )
        {
            Interlocked.Increment(ref _calls);
            await Task.CompletedTask;

            foreach (DnsMessage message in messages)
                yield return message;
        }

        public IAsyncEnumerable<DnsMessage> CreateIxfrResponseAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        ) => CreateAxfrResponseAsync(context, stoppingToken);
    }
}
