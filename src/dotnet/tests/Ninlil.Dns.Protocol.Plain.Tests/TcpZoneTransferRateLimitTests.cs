using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.RateLimiting;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Protocol.Plain.Tests;

/// <summary>
///     This class tests that <see cref="TcpDnsListener" /> applies the rate limiter to AXFR
///     queries. It uses a real listener on a loopback port with a fake limiter and a fake
///     <see cref="IZoneTransferService" /> that counts its calls.
/// </summary>
public sealed class TcpZoneTransferRateLimitTests
{
    [Fact]
    public async Task ARateLimitedAxfr_ClosesTheConnectionWithoutAReplyOrCallingTheTransferService()
    {
        CountingTransferService service = new();
        await using TcpStreamTestHost host = await TcpStreamTestHost.StartAsync(
            TimeSpan.FromSeconds(30),
            service,
            new DenyingLimiter()
        );

        using TcpClient client = new(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        NetworkStream stream = client.GetStream();
        await stream.WriteAsync(
            TcpStreamTestHost.BuildFrame(9, "zone.example.", DnsResourceRecordTypeEnum.Axfr)
        );

        Assert.Null(await TcpStreamTestHost.ReadMessageOrEndAsync(stream));
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task AnAxfrThePermissiveLimiterAllows_IsServedAndTheConnectionCloses()
    {
        CountingTransferService service = new();
        await using TcpStreamTestHost host = await TcpStreamTestHost.StartAsync(
            TimeSpan.FromSeconds(30),
            service,
            new AllowingLimiter()
        );

        using TcpClient client = new(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        NetworkStream stream = client.GetStream();
        await stream.WriteAsync(
            TcpStreamTestHost.BuildFrame(9, "zone.example.", DnsResourceRecordTypeEnum.Axfr)
        );

        Assert.Equal(1, (await TcpStreamTestHost.ReadMessageOrEndAsync(stream)).Id);
        Assert.Null(await TcpStreamTestHost.ReadMessageOrEndAsync(stream));
        Assert.Equal(1, service.Calls);
    }

    private sealed class DenyingLimiter : IDnsRateLimiter
    {
        public ValueTask<bool> TryAcquireAsync(
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default
        ) => ValueTask.FromResult(false);

        public ValueTask<DnsSubnetRateLimitDecisionEnum> CheckSubnetLimitAsync(
            IPAddress remoteAddress,
            bool isUdp,
            CancellationToken stoppingToken = default
        ) => ValueTask.FromResult(DnsSubnetRateLimitDecisionEnum.Allow);
    }

    private sealed class AllowingLimiter : IDnsRateLimiter
    {
        public ValueTask<bool> TryAcquireAsync(
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default
        ) => ValueTask.FromResult(true);

        public ValueTask<DnsSubnetRateLimitDecisionEnum> CheckSubnetLimitAsync(
            IPAddress remoteAddress,
            bool isUdp,
            CancellationToken stoppingToken = default
        ) => ValueTask.FromResult(DnsSubnetRateLimitDecisionEnum.Allow);
    }

    /// <summary>
    ///     This service counts calls and yields one message with id 1.
    /// </summary>
    private sealed class CountingTransferService : IZoneTransferService
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
            yield return new DnsMessage
            {
                Id = 1,
                IsResponse = true,
                ResponseCode = DnsResponseCodeEnum.NoError,
            };
        }

        public IAsyncEnumerable<DnsMessage> CreateIxfrResponseAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        ) => CreateAxfrResponseAsync(context, stoppingToken);
    }
}
