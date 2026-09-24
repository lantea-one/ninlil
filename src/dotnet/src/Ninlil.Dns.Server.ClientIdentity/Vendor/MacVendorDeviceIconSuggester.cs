using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Vendor;

/// <summary>
///     This static class suggests a device icon from a hardware vendor's name -- a small,
///     hand-written heuristic, deliberately conservative. A vendor maps to a suggestion only when
///     nearly everything it puts on a home network is one kind of device (a printer maker, a
///     single-board-computer foundation, a speaker company). Vendors whose products span many kinds
///     (the large phone/laptop/TV conglomerates, or a network chip supplier whose silicon ends up in
///     anything) deliberately get no suggestion at all, since a wrong icon is worse than the
///     neutral one.
///     <para>
///         A suggestion is only ever computed at read time and shown as such; it is never written
///         to a client row, so an operator's own explicit choice always takes precedence and this
///         list can be improved without touching any stored data.
///     </para>
/// </summary>
public static class MacVendorDeviceIconSuggester
{
    /// <summary>
    ///     This field contains every rule, as a case-insensitive substring of the IEEE-published
    ///     organization name and the icon it suggests, checked in order (the first match wins).
    /// </summary>
    private static readonly (string VendorFragment, DnsClientDeviceIconEnum Icon)[] Rules =
    [
        // Single-board computers.
        ("Raspberry Pi", DnsClientDeviceIconEnum.SingleBoardComputer),
        // Printers and scanners.
        ("Brother Industries", DnsClientDeviceIconEnum.Printer),
        ("Seiko Epson", DnsClientDeviceIconEnum.Printer),
        ("Lexmark", DnsClientDeviceIconEnum.Printer),
        ("Xerox", DnsClientDeviceIconEnum.Printer),
        ("Kyocera Document", DnsClientDeviceIconEnum.Printer),
        // Speakers.
        ("Sonos Inc", DnsClientDeviceIconEnum.Speaker),
        ("Sonos, Inc", DnsClientDeviceIconEnum.Speaker),
        ("SONOS Co", DnsClientDeviceIconEnum.Speaker),
        ("Bose Corporation", DnsClientDeviceIconEnum.Speaker),
        // Televisions and streaming players.
        ("Roku", DnsClientDeviceIconEnum.Television),
        ("Vizio, Inc", DnsClientDeviceIconEnum.Television),
        ("Hisense Visual", DnsClientDeviceIconEnum.Television),
        ("Hisense Electric", DnsClientDeviceIconEnum.Television),
        // Game consoles.
        ("Nintendo", DnsClientDeviceIconEnum.GameConsole),
        ("Sony Interactive Entertainment", DnsClientDeviceIconEnum.GameConsole),
        ("Valve Corporation", DnsClientDeviceIconEnum.GameConsole),
        // Network cameras and doorbells.
        ("Hikvision", DnsClientDeviceIconEnum.Camera),
        ("Dahua Technology", DnsClientDeviceIconEnum.Camera),
        ("Axis Communications", DnsClientDeviceIconEnum.Camera),
        ("Reolink", DnsClientDeviceIconEnum.Camera),
        ("Arlo Technolog", DnsClientDeviceIconEnum.Camera),
        // Wearables.
        ("Garmin", DnsClientDeviceIconEnum.Watch),
        ("Fitbit", DnsClientDeviceIconEnum.Watch),
        // Smart-home devices.
        ("Espressif", DnsClientDeviceIconEnum.SmartHome),
        ("Tuya", DnsClientDeviceIconEnum.SmartHome),
        ("Signify", DnsClientDeviceIconEnum.SmartHome),
        ("Philips Lighting", DnsClientDeviceIconEnum.SmartHome),
        ("Shelly", DnsClientDeviceIconEnum.SmartHome),
        ("ecobee", DnsClientDeviceIconEnum.SmartHome),
        ("Nest Labs", DnsClientDeviceIconEnum.SmartHome),
        // Servers, NAS appliances, and virtualization.
        ("Synology", DnsClientDeviceIconEnum.Server),
        ("QNAP", DnsClientDeviceIconEnum.Server),
        ("VMware", DnsClientDeviceIconEnum.Server),
        ("Proxmox", DnsClientDeviceIconEnum.Server),
        ("Super Micro Computer", DnsClientDeviceIconEnum.Server),
        // Wireless access points.
        ("Ubiquiti", DnsClientDeviceIconEnum.AccessPoint),
        ("Ruckus", DnsClientDeviceIconEnum.AccessPoint),
        // Routers and gateways.
        ("Routerboard", DnsClientDeviceIconEnum.Router),
        ("AVM Audiovisuelles", DnsClientDeviceIconEnum.Router),
        ("AVM GmbH", DnsClientDeviceIconEnum.Router),
        ("NETGEAR", DnsClientDeviceIconEnum.Router),
        ("TP-LINK", DnsClientDeviceIconEnum.Router),
        ("eero", DnsClientDeviceIconEnum.Router),
        ("Juniper Networks", DnsClientDeviceIconEnum.Router),
        // Network switches.
        ("Cisco Systems", DnsClientDeviceIconEnum.NetworkSwitch),
        // Phones.
        ("OnePlus", DnsClientDeviceIconEnum.Phone),
        ("Motorola Mobility", DnsClientDeviceIconEnum.Phone),
        ("HMD Global", DnsClientDeviceIconEnum.Phone),
        ("Fairphone", DnsClientDeviceIconEnum.Phone),
    ];

    /// <summary>
    ///     This method suggests an icon for a vendor.
    /// </summary>
    /// <param name="vendor">The IEEE-published organization name, or <see langword="null" />.</param>
    /// <returns>The suggested icon, or <see langword="null" /> when the vendor gives no reliable hint.</returns>
    public static DnsClientDeviceIconEnum? Suggest(string vendor)
    {
        if (string.IsNullOrWhiteSpace(vendor))
            return null;

        foreach ((string fragment, DnsClientDeviceIconEnum icon) in Rules)
        {
            if (vendor.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return icon;
        }

        return null;
    }
}
