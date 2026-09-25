using System.Net;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Notify;

/// <summary>
///     This interface sends one RFC 1996 NOTIFY to a secondary and waits for the
///     acknowledgement, retrying as <see cref="NotifyOptions" /> says. Here the server acts as a
///     client, so it needs only an outbound socket, not an inbound transport.
///     <see cref="ZoneNotifyEventSubscriber" /> uses it; the implementation is
///     <see cref="UdpNotifySender" />.
/// </summary>
public interface INotifySender
{
    /// <summary>
    ///     This method sends a NOTIFY and waits for a matching acknowledgement, retrying up to
    ///     <see cref="NotifyOptions.MaxAttempts" /> times.
    /// </summary>
    /// <param name="secondaryAddress">The secondary server to notify.</param>
    /// <param name="notifyMessage">The NOTIFY message to send, unchanged on every attempt.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     Whether a matching acknowledgement arrived before the attempts ran out.
    /// </returns>
    Task<bool> SendAsync(
        IPAddress secondaryAddress,
        DnsMessage notifyMessage,
        CancellationToken stoppingToken = default
    );
}
