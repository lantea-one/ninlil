using System.Text.Json.Serialization;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration names the seven days of the week as independent bit flags, so a single
///     value can carry an arbitrary subset of days (e.g. "school nights" = Sunday through
///     Thursday) rather than forcing a caller to store seven separate boolean columns or a
///     single-value <see cref="DayOfWeek" />. Used by <c>DnsClientScheduleEntity</c>/
///     <c>DnsClientGroupScheduleEntity</c> (<c>Ninlil.Dns.Data.Abstractions</c>) to say which days
///     a parental-control time-of-day schedule applies to.
///     <para>
///         <strong>Wire representation</strong>: this enum's own type-level
///         <see cref="JsonStringEnumConverter{TEnum}" /> attribute is the plain, built-in
///         System.Text.Json converter (not this project's own <c>EnumDescriptionJsonConverter{TEnum}</c>,
///         which reads/writes exactly one name and has no notion of combining several) --
///         <see cref="JsonStringEnumConverter{TEnum}" /> already has first-class, built-in support
///         for a <c>[Flags]</c> enum, serializing a combined value as a comma-separated list of
///         its own set member names (e.g. <c>"Monday, Tuesday, Wednesday"</c>) and parsing that
///         same shape back on the way in, so no bespoke flag-combination logic is needed here at
///         all.
///     </para>
/// </summary>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<DnsScheduleDayOfWeekEnum>))]
public enum DnsScheduleDayOfWeekEnum
{
    /// <summary>
    ///     No day at all -- never a meaningful value for a real schedule to be saved with (see
    ///     each schedule controller's own <c>TryValidate</c>), only ever the type's own default.
    /// </summary>
    None = 0,

    /// <summary>Sunday.</summary>
    Sunday = 1 << 0,

    /// <summary>Monday.</summary>
    Monday = 1 << 1,

    /// <summary>Tuesday.</summary>
    Tuesday = 1 << 2,

    /// <summary>Wednesday.</summary>
    Wednesday = 1 << 3,

    /// <summary>Thursday.</summary>
    Thursday = 1 << 4,

    /// <summary>Friday.</summary>
    Friday = 1 << 5,

    /// <summary>Saturday.</summary>
    Saturday = 1 << 6,

    /// <summary>Every day of the week at once.</summary>
    All = Sunday | Monday | Tuesday | Wednesday | Thursday | Friday | Saturday,
}
