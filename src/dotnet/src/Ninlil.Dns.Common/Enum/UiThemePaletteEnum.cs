using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the color palettes the admin console can use. Each palette has a
///     light and a dark variant, chosen by <see cref="UiThemeModeEnum" />. The value has no effect
///     on the server. The server only stores and returns it, either as the host default in
///     <c>AppearanceOptions</c> or as a user's override on <c>UserEntity</c>, and the console
///     applies it.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<UiThemePaletteEnum>>]
public enum UiThemePaletteEnum
{
    /// <summary>
    ///     The teal palette. It is used when neither the host default nor the user's override has
    ///     been set.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Teal")]
    Teal = 0,

    /// <summary>
    ///     The indigo palette.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Indigo")]
    Indigo = 1,

    /// <summary>
    ///     The amber palette.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Amber")]
    Amber = 2,

    /// <summary>
    ///     The rose palette.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Rose")]
    Rose = 3,

    /// <summary>
    ///     The emerald palette.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Emerald")]
    Emerald = 4,
}
