using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists every light/dark rendering mode the Angular admin SPA's own theming
///     engine supports, on top of whichever <see cref="UiThemePaletteEnum" /> palette is active.
///     Like <see cref="UiThemePaletteEnum" />, a value here has no server-side rendering effect of
///     its own -- this server only stores and returns it.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<UiThemeModeEnum>>]
public enum UiThemeModeEnum
{
    /// <summary>
    ///     Always render the active palette's own light variant, regardless of the browser's own
    ///     operating-system preference.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Light")]
    Light = 0,

    /// <summary>
    ///     Always render the active palette's own dark variant, regardless of the browser's own
    ///     operating-system preference.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Dark")]
    Dark = 1,

    /// <summary>
    ///     Follow the browser's own operating-system preference, resolved client-side via the
    ///     <c>prefers-color-scheme</c> media feature -- this server's own compiled-in default,
    ///     since it is the one choice that never fights a caller's own system-wide preference.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "System")]
    System = 2,
}
