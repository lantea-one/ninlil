// Define our namespace.
namespace Ninlil.Dns.Server.Events.RabbitMq;

/// <summary>
///     This class binds to the <c>DnsServer:Events:RabbitMq</c> section of the application's
///     configuration and holds the connection and topology settings of
///     <see cref="RabbitMqDnsServerEventBrokerTransport" />. It is read only when
///     <c>DnsServer:Events:Transport</c> is <see cref="Common.Enum.EventTransportEnum.RabbitMq" />.
/// </summary>
public sealed class RabbitMqEventBrokerOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Events:RabbitMq";

    /// <summary>
    ///     This property contains the RabbitMQ broker's host name or address.
    /// </summary>
    public string HostName { get; set; } = "localhost";

    /// <summary>
    ///     This property contains the RabbitMQ broker's AMQP port.
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
    ///     This property contains the name of the shared fanout exchange every connected Ninlil
    ///     instance publishes to and consumes from. A fanout exchange, unlike a work queue, gives
    ///     every instance, including the publisher, every event, as
    ///     <see cref="Server.Core.Events.Broker.IDnsServerEventBrokerTransport" /> requires.
    /// </summary>
    public string ExchangeName { get; set; } = "ninlil-dns-events";

    /// <summary>
    ///     This property contains how long this transport waits before its first reconnect
    ///     attempt after the connection to the broker is lost.
    /// </summary>
    public TimeSpan InitialReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     This property contains the longest this transport ever waits between reconnect
    ///     attempts. The delay starts at <see cref="InitialReconnectDelay" /> and doubles after
    ///     each failed attempt up to this ceiling, so a long outage is not retried in a tight
    ///     loop.
    /// </summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);
}
