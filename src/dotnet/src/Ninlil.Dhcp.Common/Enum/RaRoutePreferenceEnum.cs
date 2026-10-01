using System.Text.Json.Serialization;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Enum;

/// <summary>
///     This enumeration contains the three valid RFC 4191 Section 2.1 route preferences, carried
///     as a signed two-bit field in a Route Information option
///     (<see cref="Wire.Ra.RaRouteInformationOption" />). Each member's value is the two-bit wire
///     encoding (<c>00</c> for <see cref="Medium" />, <c>01</c> for <see cref="High" />,
///     <c>11</c> for <see cref="Low" />), so the codec can write it with a plain cast.
///     <para>
///         RFC 4191 Section 2.1 reserves the pattern <c>10</c> ("MUST NOT be sent"). It has no
///         member here, and <see cref="Wire.Ra.RaRouteInformationOption.TryDecode" /> rejects an
///         option that carries it.
///     </para>
///     <para>
///         The REST API sends this as its member name. The converter sits on the type rather than
///         on each DTO property so the OpenAPI document describes it as a string enum too:
///         Swashbuckle reads a type-level converter but ignores a property-level one.
///     </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RaRoutePreferenceEnum>))]
public enum RaRoutePreferenceEnum : byte
{
    /// <summary>
    ///     RFC 4191 Section 2.1's "Medium" route preference: the preference of an ordinary default
    ///     router, and the default for a newly configured route.
    /// </summary>
    Medium = 0b00,

    /// <summary>RFC 4191 Section 2.1's "High" route preference.</summary>
    High = 0b01,

    /// <summary>RFC 4191 Section 2.1's "Low" route preference.</summary>
    Low = 0b11,
}
