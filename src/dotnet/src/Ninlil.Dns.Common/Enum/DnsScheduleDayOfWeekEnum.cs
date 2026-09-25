using System.Text.Json.Serialization;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration names the seven days of the week as bit flags, so one value can hold any
///     set of days (for example "school nights", Sunday through Thursday) instead of seven boolean
///     columns or a single <see cref="DayOfWeek" />. <c>DnsClientScheduleEntity</c> and
///     <c>DnsClientGroupScheduleEntity</c> use it to say which days a parental-control schedule
///     applies to.
///     <para>
///         It uses the built-in <see cref="JsonStringEnumConverter{TEnum}" /> rather than
///         <c>EnumDescriptionJsonConverter{TEnum}</c>, which handles one name only. The built-in
///         converter writes a combined value as a comma-separated list of member names (for
///         example <c>"Monday, Tuesday, Wednesday"</c>) and parses the same form.
///     </para>
/// </summary>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<DnsScheduleDayOfWeekEnum>))]
public enum DnsScheduleDayOfWeekEnum
{
    /// <summary>
    ///     No day. It is the type's default and is not valid for a saved schedule (see each schedule
    ///     controller's <c>TryValidate</c>).
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
