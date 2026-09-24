using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists every color palette the Angular admin SPA's own theming engine can
///     render the application in -- five hand-tuned, WCAG-AA-checked palettes, each with its own
///     light and dark variant. A value here carries no server-side rendering behavior of its own;
///     this server only stores and returns the caller's chosen palette (as this host's own global
///     default, in <c>AppearanceOptions</c>, or as a signed-in user's own override, on
///     <c>UserEntity</c>) for the SPA's own theming engine to apply.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<UiThemePaletteEnum>>]
public enum UiThemePaletteEnum
{
    /// <summary>
    ///     The teal palette -- this server's own compiled-in default, applied whenever neither
    ///     this host's own global default nor a signed-in user's own override has ever been set.
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
