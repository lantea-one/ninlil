using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Notify;

/// <summary>
///     This class implements <see cref="INotifySender" /> over plain UDP, per RFC 1996's own
///     convention -- a NOTIFY and its acknowledgement are always small enough to need nothing
///     more.
/// </summary>
/// <param name="options">
///     This sender's own configuration, such as retry count and timeout -- bound via
///     <see cref="IOptionsMonitor{TOptions}" /> and read fresh, via <c>options.CurrentValue</c>,
///     on every single <see cref="SendAsync" /> call, so a configuration write reaches the very
///     next NOTIFY this (scoped) sender attempts, with no restart.
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
    ///     This method performs a single send-and-await-acknowledgement attempt.
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
            // A secondary that is slow to answer, unreachable, or sends back something that
            // doesn't parse or doesn't match this NOTIFY is all treated the same way: this
            // attempt did not get acknowledged, so the caller should retry rather than fail
            // outright on the first hiccup.
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
