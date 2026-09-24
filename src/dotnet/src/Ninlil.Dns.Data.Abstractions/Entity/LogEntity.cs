using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single structured log entry captured from the application's
///     logging pipeline and written to the database, so that an administrator can search and
///     filter server history from the administration console instead of shelling into the host to
///     read a log file.
/// </summary>
public class LogEntity : EntityBase
{
    /// <summary>
    ///     This property contains the UTC date and time at which the event described by this log
    ///     entry actually occurred. This can differ slightly from <see cref="EntityBase.CreatedAtUtc" />
    ///     when log writes are buffered and flushed in a batch.
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
