using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes how severe a persisted log entry is. It deliberately mirrors
///     the six levels <see cref="Microsoft.Extensions.Logging.LogLevel" /> already defines, so
///     that a log entry read from the ASP.NET Core logging pipeline maps onto it without any loss
///     of meaning, while still giving Ninlil its own type to attach storage- and
///     serialization-specific attributes to.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<LogSeverityEnum>>]
public enum LogSeverityEnum
{
    /// <summary>
    ///     The most granular detail, typically only enabled while chasing down a specific problem.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Trace")]
    Trace = 0,

    /// <summary>
    ///     Detail useful during development and diagnostics, but too noisy for routine operation.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Debug")]
    Debug = 1,

    /// <summary>
    ///     Records the normal, expected flow of the application.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Information")]
    Information = 2,

    /// <summary>
    ///     Something unexpected happened, but the application can continue running.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Warning")]
    Warning = 3,

    /// <summary>
    ///     An operation failed. The application keeps running, but that specific request or task
    ///     did not succeed.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Error")]
    Error = 4,

    /// <summary>
    ///     A failure severe enough that the application, or a major part of it, cannot continue
    ///     operating normally.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Critical")]
    Critical = 5,
}
