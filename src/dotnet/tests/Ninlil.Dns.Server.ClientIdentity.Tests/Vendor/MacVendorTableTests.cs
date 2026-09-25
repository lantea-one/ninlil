using Ninlil.Dns.Server.ClientIdentity.Vendor;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Tests.Vendor;

/// <summary>
///     This class covers <see cref="MacVendorTable" />'s longest-prefix matching,
///     <see cref="MacVendorSnapshotCodec" />'s round trip, and the snapshot embedded in this
///     build.
/// </summary>
public class MacVendorTableTests
{
    /// <summary>
    ///     This method builds a table nesting an MA-S block inside an MA-M block inside an MA-L
    ///     block, the way IEEE Registration Authority blocks nest.
    /// </summary>
    private static MacVendorTable BuildNestedTable() =>
        new(
            [
                new(MacVendorRegistryEnum.MaL, 0x8C1F64, "IEEE Registration Authority"),
                new(MacVendorRegistryEnum.MaM, 0x8C1F641, "Medium Block Holder"),
                new(MacVendorRegistryEnum.MaS, 0x8C1F64AFA, "Small Block Holder"),
                new(MacVendorRegistryEnum.MaL, 0x001132, "Synology Incorporated"),
            ],
            DateTimeOffset.Parse("2026-09-23T00:00:00Z"),
            MacVendorDataSourceEnum.Downloaded
        );

    [Theory]
    [InlineData("8c1f64afa123", "Small Block Holder")] // MA-S wins over MA-M/MA-L
    [InlineData("8c1f641abcde", "Medium Block Holder")] // MA-M wins over MA-L
    [InlineData("8c1f64fff000", "IEEE Registration Authority")] // falls back to MA-L
    [InlineData("00113200aabb", "Synology Incorporated")]
    [InlineData("0011330000aa", null)] // no registry lists this block
    public void Lookup_PrefersTheLongestMatchingPrefix(string macHex, string expected)
    {
        Assert.Equal(expected, BuildNestedTable().Lookup(Convert.FromHexString(macHex)));
    }

    [Fact]
    public void Lookup_RejectsAnythingButASixOctetAddress()
    {
        Assert.Null(BuildNestedTable().Lookup(Convert.FromHexString("001132")));
        Assert.Null(BuildNestedTable().Lookup(Convert.FromHexString("00113200aabbccdd")));
    }

    [Fact]
    public void Codec_RoundTripsEveryPrefixNameGenerationDateAndValidator()
    {
        MacVendorTable original = new(
            [
                new(MacVendorRegistryEnum.MaL, 0x001132, "Synology Incorporated"),
                new(MacVendorRegistryEnum.MaL, 0x0011D8, "Synology Incorporated"),
                new(MacVendorRegistryEnum.MaM, 0xC85CE27, "Commas, Inc., Ltd."),
                new(MacVendorRegistryEnum.MaS, 0x8C1F64EAA, "#Hash-Leading \"Quoted\" Name"),
            ],
            DateTimeOffset.Parse("2026-09-23T17:20:47.1234567Z"),
            MacVendorDataSourceEnum.Downloaded,
            new Dictionary<MacVendorRegistryEnum, MacVendorRegistryValidator>
            {
                [MacVendorRegistryEnum.MaL] = new(
                    "\"6ab3f7ec-3aa88a\"",
                    "Wed, 23 Sep 2026 16:01:48 GMT"
                ),
                [MacVendorRegistryEnum.MaS] = new(null, "Wed, 23 Sep 2026 16:02:41 GMT"),
            }
        );

        using MemoryStream stream = new();
        MacVendorSnapshotCodec.Write(stream, original);
        stream.Position = 0;
        MacVendorTable copy = MacVendorSnapshotCodec.Read(stream, MacVendorDataSourceEnum.Embedded);

        Assert.Equal(original.GeneratedAtUtc, copy.GeneratedAtUtc);
        Assert.Equal(MacVendorDataSourceEnum.Embedded, copy.Source);
        foreach (MacVendorRegistryEnum registry in MacVendorRegistryExtension.All)
            Assert.Equal(
                original.GetAssignments(registry).OrderBy(a => a.Prefix),
                copy.GetAssignments(registry).OrderBy(a => a.Prefix)
            );

        Assert.Equal(
            original.Validators[MacVendorRegistryEnum.MaL],
            copy.Validators[MacVendorRegistryEnum.MaL]
        );
        Assert.Equal(
            original.Validators[MacVendorRegistryEnum.MaS],
            copy.Validators[MacVendorRegistryEnum.MaS]
        );
        Assert.False(copy.Validators.ContainsKey(MacVendorRegistryEnum.MaM));
    }

    [Fact]
    public void Codec_RejectsAStreamThatIsNotASnapshot()
    {
        using MemoryStream stream = new("not a snapshot"u8.ToArray());
        Assert.ThrowsAny<Exception>(() =>
            MacVendorSnapshotCodec.Read(stream, MacVendorDataSourceEnum.Downloaded)
        );
    }

    [Fact]
    public void EmbeddedSnapshot_LoadsAllThreeRegistriesAndResolvesKnownBlocks()
    {
        MacVendorTable table = MacVendorLookupService.LoadEmbeddedSnapshot();

        Assert.Equal(MacVendorDataSourceEnum.Embedded, table.Source);
        foreach (MacVendorRegistryEnum registry in MacVendorRegistryExtension.All)
            Assert.True(
                table.Count(registry) >= MacVendorRefreshService.GetMinimumEntryCount(registry),
                $"{registry.IeeeName} has only {table.Count(registry)} entries."
            );

        // B8:27:EB is the Raspberry Pi Foundation's classic MA-L block; 00:11:32 is Synology's.
        Assert.Contains("Raspberry Pi", table.Lookup(Convert.FromHexString("b827eb000001")));
        Assert.Contains("Synology", table.Lookup(Convert.FromHexString("001132000001")));
        Assert.Equal(3, table.Validators.Count);
    }
}
