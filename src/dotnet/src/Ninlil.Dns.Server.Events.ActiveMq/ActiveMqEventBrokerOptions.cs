// Define our namespace.
namespace Ninlil.Dns.Server.Events.ActiveMq;

/// <summary>
///     This class binds to the <c>DnsServer:Events:ActiveMq</c> section of the application's
///     configuration and holds the connection and topology settings of
///     <see cref="ActiveMqDnsServerEventBrokerTransport" />. It is read only when
///     <c>DnsServer:Events:Transport</c> is <see cref="Common.Enum.EventTransportEnum.ActiveMq" />.
/// </summary>
public sealed class ActiveMqEventBrokerOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Events:ActiveMq";

    /// <summary>
    ///     This property contains the broker's host name or address.
    /// </summary>
    public string HostName { get; set; } = "localhost";

    /// <summary>
    ///     This property contains the broker's OpenWire port.
    /// </summary>
    public int Port { get; set; } = 61616;

    /// <summary>
    ///     This property contains the username this transport authenticates with.
    /// </summary>
    public string UserName { get; set; } = "admin";

    /// <summary>
    ///     This property contains the password this transport authenticates with.
    /// </summary>
    public string Password { get; set; } = "admin";

    /// <summary>
    ///     This property contains the name of the shared topic every connected Ninlil instance
    ///     publishes to and subscribes to. It must be a topic: a JMS queue delivers each message
    ///     to only one consumer, while
    ///     <see cref="Server.Core.Events.Broker.IDnsServerEventBrokerTransport" /> requires every
    ///     instance, including the publisher, to receive every event.
    /// </summary>
    public string TopicName { get; set; } = "ninlil-dns-events";

    /// <summary>
    ///     This property contains how long this transport waits before its first reconnect
    ///     attempt after the connection to the broker is lost. Unlike
    ///     <c>Server.Events.RabbitMq</c>, this transport has no reconnect loop of its own: the
    ///     value becomes the <c>initialReconnectDelay</c> parameter of the NMS <c>failover:</c>
    ///     URI, and the client library backs off from there up to
    ///     <see cref="MaxReconnectDelay" />.
    /// </summary>
    public TimeSpan InitialReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     This property contains the longest this transport ever waits between reconnect
    ///     attempts, passed as the <c>maxReconnectDelay</c> parameter of the <c>failover:</c> URI.
    /// </summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This property bounds how long a single <see cref="ActiveMqDnsServerEventBrokerTransport.PublishAsync" />
    ///     call may block waiting for the failover transport to have a live connection, passed as
    ///     the <c>timeout</c> parameter of the <c>failover:</c> URI. Without it, the NMS client
    ///     holds a send indefinitely while disconnected. Ninlil does not buffer events, so this
    ///     should be short enough that an event published during an outage is dropped and logged
    ///     promptly.
    /// </summary>
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(2);
}
