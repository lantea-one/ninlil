using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.ClientIdentity.Vendor;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Tests.Vendor;

/// <summary>
///     This class covers <see cref="MacVendorDeviceIconSuggester" />: a suggestion for a
///     single-purpose vendor (spelled exactly as the IEEE publishes it), and deliberately none for
///     a vendor whose products span many kinds of device.
/// </summary>
public class MacVendorDeviceIconSuggesterTests
{
    [Theory]
    [InlineData("Raspberry Pi Trading Ltd", DnsClientDeviceIconEnum.SingleBoardComputer)]
    [InlineData("Brother Industries, LTD.", DnsClientDeviceIconEnum.Printer)]
    [InlineData("Sonos, Inc.", DnsClientDeviceIconEnum.Speaker)]
    [InlineData("Roku, Inc", DnsClientDeviceIconEnum.Television)]
    [InlineData("Nintendo Co.,Ltd", DnsClientDeviceIconEnum.GameConsole)]
    [InlineData("Espressif Inc.", DnsClientDeviceIconEnum.SmartHome)]
    [InlineData("Synology Incorporated", DnsClientDeviceIconEnum.Server)]
    [InlineData("Ubiquiti Inc", DnsClientDeviceIconEnum.AccessPoint)]
    [InlineData("TP-LINK TECHNOLOGIES CO.,LTD.", DnsClientDeviceIconEnum.Router)]
    public void Suggest_MapsASinglePurposeVendorToItsIcon(
        string vendor,
        DnsClientDeviceIconEnum expected
    ) => Assert.Equal(expected, MacVendorDeviceIconSuggester.Suggest(vendor));

    [Theory]
    [InlineData("Apple, Inc.")]
    [InlineData("Samsung Electronics Co.,Ltd")]
    [InlineData("Intel Corporate")]
    [InlineData("SonoSite, Inc.")] // shares a prefix with "Sonos", but is a medical-imaging maker
    [InlineData("")]
    [InlineData(null)]
    public void Suggest_GivesNoSuggestionForAnAmbiguousOrUnknownVendor(string vendor) =>
        Assert.Null(MacVendorDeviceIconSuggester.Suggest(vendor));
}
