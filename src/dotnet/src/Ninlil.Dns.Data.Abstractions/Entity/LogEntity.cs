using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one structured log entry from the application's logging pipeline,
///     stored in the database so an administrator can search server history in the admin
///     console instead of reading log files on the host.
/// </summary>
public class LogEntity : EntityBase
{
    /// <summary>
    ///     This property contains the UTC date and time the logged event occurred. It can differ
    ///     slightly from <see cref="EntityBase.CreatedAtUtc" /> when log writes are batched.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; set; }

    /// <summary>
    ///     This property contains how severe the logged event was.
    /// </summary>
    public LogSeverityEnum Severity { get; set; }

    /// <summary>
    ///     This property contains the logger category that produced the entry, typically the
    ///     fully-qualified name of the class doing the logging.
    /// </summary>
    public string Category { get; set; }

    /// <summary>
    ///     This property contains the rendered, human-readable log message.
    /// </summary>
    public string Message { get; set; }

    /// <summary>
    ///     This property contains the exception's details (type, message, and stack trace), or
    ///     <see langword="null" /> if the entry was not logged alongside an exception.
    /// </summary>
    public string Exception { get; set; }

    /// <summary>
    ///     This property contains any additional structured properties attached to the log entry
    ///     (for example, a client IP address or a query name), serialized as a JSON object.
    /// </summary>
    public string PropertiesJson { get; set; }
}
