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
///     This class tests that the connection of a zone transfer over <see cref="TcpDnsListener" />
///     ends in an orderly close even when the client sent one more message that the server never
///     read. A close with unread data is a reset, and on Windows a reset makes the client discard
///     the last transfer message it had received but not yet read. The test uses real loopback
///     sockets and a fake <see cref="IZoneTransferService" /> that holds its last message until
///     the test has put the extra message on the server's socket.
/// </summary>
public sealed class TcpZoneTransferCloseTests
{
    [Fact]
    public async Task AnAxfrClosedWithAnUnreadClientMessage_StillDeliversItsLastMessage()
    {
        GatedTransferService service = new();
        await using TcpStreamTestHost host = await TcpStreamTestHost.StartAsync(
            TimeSpan.FromSeconds(30),
            service
        );

        // A reset can be lost in the race with the client's read, so repeat the scenario.
        for (int round = 0; round < 20; round++)
        {
            service.ReleaseLastMessage = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            using TcpClient client = new(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, host.Port);
            NetworkStream stream = client.GetStream();
            await stream.WriteAsync(
                TcpStreamTestHost.BuildFrame(9, "zone.example.", DnsResourceRecordTypeEnum.Axfr)
            );

            Assert.Equal(1, (await TcpStreamTestHost.ReadMessageOrEndAsync(stream)).Id);

            // The session is inside the transfer and does not read; this frame stays unread in
            // the server's receive queue until the connection closes.
            await stream.WriteAsync(TcpStreamTestHost.BuildFrame(10, "other.example."));
            await Task.Delay(100);
            service.ReleaseLastMessage.SetResult();

            DnsMessage last = await TcpStreamTestHost.ReadMessageOrEndAsync(stream);
            Assert.NotNull(last);
            Assert.Equal(2, last.Id);
            Assert.Null(await TcpStreamTestHost.ReadMessageOrEndAsync(stream));
        }
    }

    /// <summary>
    ///     This service yields two messages (ids 1 and 2) and holds the second until the test
    ///     sets <see cref="ReleaseLastMessage" />.
    /// </summary>
    private sealed class GatedTransferService : IZoneTransferService
    {
        public TaskCompletionSource ReleaseLastMessage { get; set; }

        public async IAsyncEnumerable<DnsMessage> CreateAxfrResponseAsync(
            DnsQueryContext context,
            [EnumeratorCancellation] CancellationToken stoppingToken = default
        )
        {
            yield return Message(1);
            await ReleaseLastMessage.Task.WaitAsync(stoppingToken);
            yield return Message(2);
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
