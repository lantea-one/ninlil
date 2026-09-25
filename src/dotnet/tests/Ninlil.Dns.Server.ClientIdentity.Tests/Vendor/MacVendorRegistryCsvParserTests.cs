using Ninlil.Dns.Server.ClientIdentity.Vendor;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Tests.Vendor;

/// <summary>
///     This class covers <see cref="MacVendorRegistryCsvParser" /> against the shapes the IEEE
///     registry files use: RFC 4180 quoting (commas and doubled quotes inside a quoted name),
///     CRLF line endings, a line break inside a quoted address and each registry's prefix width,
///     plus the rejections that keep a bad download from being trusted.
/// </summary>
public class MacVendorRegistryCsvParserTests
{
    /// <summary>The IEEE header row, as published.</summary>
    private const string Header = "Registry,Assignment,Organization Name,Organization Address\r\n";

    [Fact]
    public void Parse_MaL_HandlesQuotedCommasDoubledQuotesAndCrlf()
    {
        string csv =
            Header
            + "MA-L,286FB9,\"Nokia Shanghai Bell Co., Ltd.\",\"No.388 Ning Qiao Road,Jin Qiao \"\r\n"
            + "MA-L,38E2CA,Katun Corporation,7760 France Ave S Suite 340 Bloomington MN US 55438 \r\n"
            + "MA-L,388A21,\"UAB \"\"Teltonika Telematics\"\"\",Vilnius LT\r\n";

        MacVendorRegistryParseResult result = MacVendorRegistryCsvParser.Parse(
            new StringReader(csv),
            MacVendorRegistryEnum.MaL
        );

        Assert.Equal(3, result.RowCount);
        Assert.Equal(0, result.MalformedRowCount);
        Assert.Equal(
            [
                new MacVendorAssignment(
                    MacVendorRegistryEnum.MaL,
                    0x286FB9,
                    "Nokia Shanghai Bell Co., Ltd."
                ),
                new MacVendorAssignment(MacVendorRegistryEnum.MaL, 0x38E2CA, "Katun Corporation"),
                new MacVendorAssignment(
                    MacVendorRegistryEnum.MaL,
                    0x388A21,
                    "UAB \"Teltonika Telematics\""
                ),
            ],
            result.Assignments
        );
    }

    [Fact]
    public void Parse_MaMAndMaS_UseTheirOwnPrefixWidths()
    {
        MacVendorRegistryParseResult medium = MacVendorRegistryCsvParser.Parse(
            new StringReader(
                Header + "MA-M,C85CE27,SYNERGY SYSTEMS AND SOLUTIONS,\"A1526, Faridabad\"\r\n"
            ),
            MacVendorRegistryEnum.MaM
        );
        MacVendorRegistryParseResult small = MacVendorRegistryCsvParser.Parse(
            new StringReader(
                Header
                    + "MA-S,8C1F64EAA,\"\"\"KB \"\"Modul\"\", LLC\",Verejskaya str. Moscow RU\r\n"
            ),
            MacVendorRegistryEnum.MaS
        );

        Assert.Equal(0xC85CE27UL, Assert.Single(medium.Assignments).Prefix);
        MacVendorAssignment smallAssignment = Assert.Single(small.Assignments);
        Assert.Equal(0x8C1F64EAAUL, smallAssignment.Prefix);
        Assert.Equal("\"KB \"Modul\", LLC", smallAssignment.Organization);
        Assert.Equal("8C1F64EAA", smallAssignment.FormatPrefix());
    }

    [Fact]
    public void Parse_ALineBreakInsideAQuotedAddress_DoesNotSplitTheRecord()
    {
        string csv = Header + "MA-L,001132,Synology Incorporated,\"Line one\r\nLine two\"\r\n";

        MacVendorRegistryParseResult result = MacVendorRegistryCsvParser.Parse(
            new StringReader(csv),
            MacVendorRegistryEnum.MaL
        );

        Assert.Equal(1, result.RowCount);
        Assert.Equal("Synology Incorporated", Assert.Single(result.Assignments).Organization);
    }

    [Fact]
    public void Parse_CountsMalformedRowsAndKeepsTheFirstOfADuplicatedPrefix()
    {
        string csv =
            Header
            + "MA-L,001132,Synology Incorporated,addr\r\n"
            + "MA-M,001133,Wrong Registry,addr\r\n" // registry column mismatch
            + "MA-L,0011,Too Short,addr\r\n" // wrong prefix width
            + "MA-L,00113Z,Not Hex,addr\r\n" // not hexadecimal
            + "MA-L,001134,   ,addr\r\n" // blank organization
            + "MA-L,001132,Duplicate Holder,addr\r\n" // duplicate: first wins
            + "\r\n"; // a blank trailing line is not a row

        MacVendorRegistryParseResult result = MacVendorRegistryCsvParser.Parse(
            new StringReader(csv),
            MacVendorRegistryEnum.MaL
        );

        Assert.Equal(6, result.RowCount);
        Assert.Equal(4, result.MalformedRowCount);
        Assert.Equal("Synology Incorporated", Assert.Single(result.Assignments).Organization);
    }

    [Theory]
    [InlineData("<!DOCTYPE html><html><body>Service Unavailable</body></html>")]
    [InlineData("")]
    [InlineData("Assignment,Registry,Organization Name\r\nMA-L,001132,x\r\n")]
    public void Parse_WithoutTheIeeeHeader_RejectsTheWholeFile(string content)
    {
        Assert.Throws<FormatException>(() =>
            MacVendorRegistryCsvParser.Parse(new StringReader(content), MacVendorRegistryEnum.MaL)
        );
    }

    [Fact]
    public void NormalizeOrganization_CollapsesControlCharactersAndWhitespace()
    {
        Assert.Equal(
            "Acme Widgets Inc.",
            MacVendorRegistryCsvParser.NormalizeOrganization("  Acme\tWidgets \r\n Inc. ")
        );
        Assert.Equal(string.Empty, MacVendorRegistryCsvParser.NormalizeOrganization(" \t "));
    }
}
