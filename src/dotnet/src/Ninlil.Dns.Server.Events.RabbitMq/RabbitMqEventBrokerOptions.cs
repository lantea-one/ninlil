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
    ///     This constant contains the Data Protection purpose string <see cref="PasswordEncrypted" />
    ///     is protected with. A purpose of its own keeps a payload protected for another secret
    ///     from decrypting as this one, so a value copied between two settings fails closed.
    /// </summary>
    public const string PasswordProtectorPurpose = "Ninlil.Dns.Server.Events.RabbitMq.Password";

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
    ///     This property contains the password this transport authenticates with. The file stores
    ///     the password in <see cref="PasswordEncrypted" />, not here, so it is not readable from
    ///     the file or from a backup of it. A non-empty value from the configuration (for example
    ///     the <c>NINLIL_DNS_SERVER_EVENTS_RABBIT_MQ_PASSWORD</c> variable) wins over
    ///     <see cref="PasswordEncrypted" />. At startup the host replaces this value with the
    ///     decrypted password. When the stored password cannot be decrypted it is set to an empty
    ///     password, so the broker refuses the login and the failure is logged, while the server
    ///     keeps running. Without any configured password the default <c>guest</c> stays.
    /// </summary>
    public string Password { get; set; } = "guest";

    /// <summary>
    ///     This property contains the password as a Data Protection payload that only this node's
    ///     key ring can read. The settings API and cluster sync write it (each node encrypts the
    ///     password with its own key ring), and the host's startup step writes it for a plaintext
    ///     password an earlier version left in the file. It is not set by hand: a payload from
    ///     another node's key ring cannot be decrypted. A non-empty <see cref="Password" /> from
    ///     the configuration wins over it.
    /// </summary>
    public string PasswordEncrypted { get; set; } = "";

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
