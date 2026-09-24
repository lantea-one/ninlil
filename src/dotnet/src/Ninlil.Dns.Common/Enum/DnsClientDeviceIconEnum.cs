using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration is the fixed catalog of device icons a known client (<c>DnsClientEntity</c>)
///     can be shown with -- Ninlil's own small, deliberately closed vocabulary of device kinds, not
///     an IANA-assigned or vendor-defined value. Each member names a kind of device rather than an
///     artwork file: the admin SPA maps every member onto one icon from the free CoreUI icon set it
///     already bundles, so the backend never depends on (or has to validate) any particular icon
///     library's own glyph names. Every member's wire name is its <c>EnumMember</c> value, and a
///     REST write naming anything outside this catalog is rejected rather than silently coerced.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsClientDeviceIconEnum>>]
public enum DnsClientDeviceIconEnum
{
    /// <summary>A device of no more specific kind -- the neutral, catch-all icon.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "Generic")]
    Generic = 0,

    /// <summary>A laptop or notebook computer.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "Laptop")]
    Laptop = 1,

    /// <summary>A desktop computer or workstation.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "Desktop")]
    Desktop = 2,

    /// <summary>A mobile phone.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "Phone")]
    Phone = 3,

    /// <summary>A tablet.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "Tablet")]
    Tablet = 4,

    /// <summary>A television, streaming box, or other media player attached to a screen.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "Television")]
    Television = 5,

    /// <summary>A router or gateway.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "Router")]
    Router = 6,

    /// <summary>A wireless access point or mesh node.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "AccessPoint")]
    AccessPoint = 7,

    /// <summary>A network switch or other wired network infrastructure.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "NetworkSwitch")]
    NetworkSwitch = 8,

    /// <summary>A printer or scanner.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "Printer")]
    Printer = 9,

    /// <summary>A game console or handheld gaming device.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "GameConsole")]
    GameConsole = 10,

    /// <summary>A networked speaker or smart speaker.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "Speaker")]
    Speaker = 11,

    /// <summary>A network camera, doorbell camera, or webcam.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "Camera")]
    Camera = 12,

    /// <summary>A smartwatch or other wearable.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "Watch")]
    Watch = 13,

    /// <summary>A smart-home device: a bulb, plug, sensor, thermostat, or similar.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "SmartHome")]
    SmartHome = 14,

    /// <summary>A server, NAS, or virtual machine host.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "Server")]
    Server = 15,

    /// <summary>A single-board computer or embedded development board.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "SingleBoardComputer")]
    SingleBoardComputer = 16,

    /// <summary>Wireless headphones or earbuds.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "Headphones")]
    Headphones = 17,

    /// <summary>A connected household appliance.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "Appliance")]
    Appliance = 18,
}
