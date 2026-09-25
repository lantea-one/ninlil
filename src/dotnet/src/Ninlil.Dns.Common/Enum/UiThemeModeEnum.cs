using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the light and dark modes the admin console's theming supports, applied
///     on top of the active <see cref="UiThemePaletteEnum" />. Like the palette, the value has no
///     effect on the server, which only stores and returns it.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<UiThemeModeEnum>>]
public enum UiThemeModeEnum
{
    /// <summary>
    ///     Always use the palette's light variant, whatever the operating system prefers.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Light")]
    Light = 0,

    /// <summary>
    ///     Always use the palette's dark variant, whatever the operating system prefers.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Dark")]
    Dark = 1,

    /// <summary>
    ///     Follow the operating-system preference, resolved in the browser through the
    ///     <c>prefers-color-scheme</c> media feature. This is the default
    ///     (<c>AppearanceOptions.Mode</c>) because it never fights the user's system-wide setting.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "System")]
    System = 2,
}
