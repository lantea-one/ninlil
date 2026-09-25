using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Notify;

/// <summary>
///     This class implements <see cref="INotifySender" /> over UDP, the RFC 1996 transport; a
///     NOTIFY and its acknowledgement are small. All attempts share one socket.
/// </summary>
/// <param name="options">
///     The retry count, timeout, delay and port, read through
///     <see cref="IOptionsMonitor{TOptions}" /> on every <see cref="SendAsync" /> call so changes
///     apply without a restart.
/// </param>
public class UdpNotifySender(IOptionsMonitor<NotifyOptions> options) : INotifySender
{
    /// <inheritdoc />
    public async Task<bool> SendAsync(
        IPAddress secondaryAddress,
        DnsMessage notifyMessage,
        CancellationToken stoppingToken = default
    )
    {
        byte[] requestBytes = DnsMessageWriter.Write(notifyMessage);
        IPEndPoint remoteEndPoint = new(secondaryAddress, options.CurrentValue.SecondaryPort);

        using UdpClient client = new(secondaryAddress.AddressFamily);

        int maxAttempts = Math.Max(1, options.CurrentValue.MaxAttempts);

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            stoppingToken.ThrowIfCancellationRequested();

            if (
                await TrySendOnceAsync(
                    client,
                    requestBytes,
                    remoteEndPoint,
                    notifyMessage.Id,
                    stoppingToken
                )
            )
                return true;

            if (attempt < maxAttempts)
                await Task.Delay(options.CurrentValue.RetryDelay, stoppingToken);
        }

        return false;
    }

    /// <summary>
    ///     This method makes one attempt: send, then wait for a matching acknowledgement.
    /// </summary>
    private async Task<bool> TrySendOnceAsync(
        UdpClient client,
        byte[] requestBytes,
        IPEndPoint remoteEndPoint,
        ushort expectedId,
        CancellationToken stoppingToken
    )
    {
        await client.SendAsync(requestBytes, remoteEndPoint, stoppingToken);

        using CancellationTokenSource timeoutCts = new(options.CurrentValue.ResponseTimeout);
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            timeoutCts.Token
        );

        try
        {
            // A timeout, an unreachable secondary or a reply that does not parse or match all
            // count as "not acknowledged", so the caller retries.
            UdpReceiveResult result = await client.ReceiveAsync(linkedCts.Token);
            DnsMessage response = DnsMessageReader.Read(result.Buffer);

            return response.IsResponse
                && response.Opcode == DnsOpcodeEnum.Notify
                && response.Id == expectedId;
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
