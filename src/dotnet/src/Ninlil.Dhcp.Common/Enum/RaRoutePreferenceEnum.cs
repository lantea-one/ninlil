// Define our namespace.
namespace Ninlil.Dhcp.Common.Enum;

/// <summary>
///     This enumeration contains RFC 4191 Section 2.1's own three legal route-preference values,
///     carried inside a Router Advertisement's Route Information option
///     (<see cref="Wire.Ra.RaRouteInformationOption" />) as a signed two-bit field. Each member's
///     own numeric value is deliberately the option's real raw two-bit wire encoding itself (not
///     an arbitrary ordinal) -- <c>00</c> for <see cref="Medium" />, <c>01</c> for
///     <see cref="High" />, <c>11</c> for <see cref="Low" /> -- so the codec can read/write this
///     field with a plain cast rather than a separate lookup table.
///     <para>
///         RFC 4191 Section 2.1 reserves the fourth two-bit pattern, <c>10</c>, and states it
///         "MUST NOT be sent" -- deliberately not a member of this enumeration at all, so an
///         attempt to encode it is a compile-time impossibility rather than a runtime check, and a
///         peer that sends it on the wire is rejected by
///         <see cref="Wire.Ra.RaRouteInformationOption.TryDecode" /> as a genuine decode failure.
///     </para>
/// </summary>
public enum RaRoutePreferenceEnum : byte
{
    /// <summary>
    ///     RFC 4191 Section 2.1's own "Medium" route preference -- the default a receiver assumes
    ///     for an ordinary default router with no Route Information option at all, and this
    ///     project's own default for a newly configured route.
    /// </summary>
    Medium = 0b00,

    /// <summary>RFC 4191 Section 2.1's own "High" route preference.</summary>
    High = 0b01,

    /// <summary>RFC 4191 Section 2.1's own "Low" route preference.</summary>
    Low = 0b11,
}
