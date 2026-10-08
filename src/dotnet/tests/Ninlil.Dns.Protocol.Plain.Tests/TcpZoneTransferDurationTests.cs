using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Protocol.Plain.Tests;

/// <summary>
///     This class tests that a zone transfer over <see cref="TcpDnsListener" /> is not cut by
///     the idle timeout. A fake <see cref="IZoneTransferService" /> produces messages with pauses
///     that add up to well over the (shortened) <see cref="PlainProtocolOptions.TcpIdleTimeout" />,
///     and the client must still receive every message and then see the connection close. The
///     transfer waits on a gate, so the test has no clock to race: it completes the gate only
///     after the idle timeout has passed several times.
/// </summary>
public sealed class TcpZoneTransferDurationTests
{
    [Fact]
    public async Task AnAxfrThatOutlivesTheIdleTimeout_Completes()
    {
        TimeSpan idle = TimeSpan.FromMilliseconds(300);
        SlowTransferService service = new();
        await using TcpStreamTestHost host = await TcpStreamTestHost.StartAsync(idle, service);

        using TcpClient client = new(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        NetworkStream stream = client.GetStream();
        await stream.WriteAsync(
            TcpStreamTestHost.BuildFrame(9, "zone.example.", DnsResourceRecordTypeEnum.Axfr)
        );

        Assert.Equal(1, (await TcpStreamTestHost.ReadMessageOrEndAsync(stream)).Id);

        // More than five idle timeouts pass in the middle of the transfer.
        await Task.Delay(idle * 5);
        service.ReleaseSecondMessage.SetResult();

        Assert.Equal(2, (await TcpStreamTestHost.ReadMessageOrEndAsync(stream)).Id);
        Assert.Equal(3, (await TcpStreamTestHost.ReadMessageOrEndAsync(stream)).Id);

        // The connection closes after the transfer, which is how the pull client sees the end.
        Assert.Null(await TcpStreamTestHost.ReadMessageOrEndAsync(stream));
        Assert.False(service.WasCancelled);
    }

    /// <summary>
    ///     This service yields three messages (ids 1, 2 and 3), holding the second until the test
    ///     lets it go.
    /// </summary>
    private sealed class SlowTransferService : IZoneTransferService
    {
        public TaskCompletionSource ReleaseSecondMessage { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasCancelled { get; private set; }

        public async IAsyncEnumerable<DnsMessage> CreateAxfrResponseAsync(
            DnsQueryContext context,
            [EnumeratorCancellation] CancellationToken stoppingToken = default
        )
        {
            yield return Message(1);

            try
            {
                await ReleaseSecondMessage.Task.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }

            yield return Message(2);
            yield return Message(3);
        }

        public IAsyncEnumerable<DnsMessage> CreateIxfrResponseAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        ) => CreateAxfrResponseAsync(context, stoppingToken);

        private static DnsMessage Message(ushort id) =>
            new()
            {
                Id = id,
                IsResponse = true,
                ResponseCode = DnsResponseCodeEnum.NoError,
            };
    }
}
