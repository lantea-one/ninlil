using System.Net;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Notify;

/// <summary>
///     This interface sends a single RFC 1996 NOTIFY message to a secondary server and waits
///     for its acknowledgement, retrying on its own terms per <see cref="NotifyOptions" />. This
///     is a real network client, not something a caller needs a live listening transport to
///     exercise -- unlike serving queries, initiating a NOTIFY is inherently this server acting
///     as a client, so it needs nothing beyond an outbound socket, independent of any inbound
///     transport such as <c>Ninlil.Dns.Protocol.Plain</c>.
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
    ///     A <see langword="bool" /> indicating whether a matching acknowledgement was received
    ///     before every attempt was exhausted.
    /// </returns>
    Task<bool> SendAsync(
        IPAddress secondaryAddress,
        DnsMessage notifyMessage,
        CancellationToken stoppingToken = default
    );
}
