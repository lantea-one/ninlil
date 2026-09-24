// Define our namespace.
namespace Ninlil.Dns.Server.Events.RabbitMq;

/// <summary>
///     This class binds to the <c>DnsServer:Events:RabbitMq</c> section of the application's
///     configuration and controls <see cref="RabbitMqDnsServerEventBrokerTransport" />'s own
///     connection and topology settings. Only consulted at all once
///     <c>DnsServer:Events:Transport</c> (<see cref="Common.Enum.EventTransportEnum.RabbitMq" />)
///     actually selects this transport.
/// </summary>
public sealed class RabbitMqEventBrokerOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Events:RabbitMq";

    /// <summary>
    ///     This property contains the RabbitMQ broker's own host name or address.
    /// </summary>
    public string HostName { get; set; } = "localhost";

    /// <summary>
    ///     This property contains the RabbitMQ broker's own AMQP port.
    /// </summary>
    public int Port { get; set; } = 5672;

    /// <summary>
    ///     This property contains the username this transport authenticates with.
    /// </summary>
    public string UserName { get; set; } = "guest";

    /// <summary>
    ///     This property contains the password this transport authenticates with.
    /// </summary>
    public string Password { get; set; } = "guest";

    /// <summary>
    ///     This property contains the virtual host this transport connects to.
    /// </summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>
    ///     This property contains the name of the shared fanout exchange every connected
    ///     Ninlil instance publishes to and consumes from -- fanout, not a work-queue
    ///     topology, so that every instance (including the publisher itself) receives every
    ///     event, matching <see cref="Server.Core.Events.Broker.IDnsServerEventBrokerTransport" />'s
    ///     own broadcast-semantics contract.
    /// </summary>
    public string ExchangeName { get; set; } = "ninlil-dns-events";

    /// <summary>
    ///     This property contains how long this transport waits before its first reconnect
    ///     attempt after the connection to the broker is lost.
    /// </summary>
    public TimeSpan InitialReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     This property contains the longest this transport ever waits between reconnect
    ///     attempts -- the reconnect delay doubles after each failed attempt, starting from
    ///     <see cref="InitialReconnectDelay" />, up to this ceiling, so a prolonged broker
    ///     outage does not result in a tight retry loop.
    /// </summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);
}
