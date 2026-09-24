// Define our namespace.
namespace Ninlil.Dns.Server.Events.ActiveMq;

/// <summary>
///     This class binds to the <c>DnsServer:Events:ActiveMq</c> section of the application's
///     configuration and controls <see cref="ActiveMqDnsServerEventBrokerTransport" />'s own
///     connection and topology settings. Only consulted at all once
///     <c>DnsServer:Events:Transport</c> (<see cref="Common.Enum.EventTransportEnum.ActiveMq" />)
///     actually selects this transport.
/// </summary>
public sealed class ActiveMqEventBrokerOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Events:ActiveMq";

    /// <summary>
    ///     This property contains the broker's own host name or address.
    /// </summary>
    public string HostName { get; set; } = "localhost";

    /// <summary>
    ///     This property contains the broker's own OpenWire port.
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
    ///     This property contains the name of the shared topic every connected Ninlil
    ///     instance publishes to and subscribes to -- a topic, not a queue, so that every
    ///     instance (including the publisher itself) receives every event, matching
    ///     <see cref="Server.Core.Events.Broker.IDnsServerEventBrokerTransport" />'s own
    ///     broadcast-semantics contract (a queue in NMS/JMS terms is competing-consumers, which
    ///     this project must not use here).
    /// </summary>
    public string TopicName { get; set; } = "ninlil-dns-events";

    /// <summary>
    ///     This property contains how long this transport waits before its first reconnect
    ///     attempt after the connection to the broker is lost. Unlike
    ///     <c>Server.Events.RabbitMq</c>, this transport does not implement its own reconnect
    ///     loop -- it is applied directly to the NMS <c>failover:</c> connection URI's own
    ///     <c>initialReconnectDelay</c> parameter, which the client library's built-in failover
    ///     transport already handles, backing off up to <see cref="MaxReconnectDelay" />.
    /// </summary>
    public TimeSpan InitialReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     This property contains the longest this transport ever waits between reconnect
    ///     attempts, applied to the <c>failover:</c> URI's own <c>maxReconnectDelay</c>
    ///     parameter.
    /// </summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This property bounds how long a single <see cref="ActiveMqDnsServerEventBrokerTransport.PublishAsync" />
    ///     call may block waiting for the failover transport to have a live connection, applied
    ///     to the <c>failover:</c> URI's own <c>timeout</c> parameter. Without this bound, the
    ///     underlying NMS client would otherwise queue a send indefinitely while disconnected --
    ///     a form of buffering this project deliberately chose not to build. Set low enough that
    ///     an event published during a real outage is dropped (and logged) promptly rather than
    ///     silently held.
    /// </summary>
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(2);
}
