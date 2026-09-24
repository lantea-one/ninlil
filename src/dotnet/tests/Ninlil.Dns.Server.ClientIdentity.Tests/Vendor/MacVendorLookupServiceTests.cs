using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.ClientIdentity.Vendor;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Tests.Vendor;

/// <summary>
///     This class covers <see cref="MacVendorLookupService" />: extracting a MAC address from every
///     stored identifier shape (a plain MAC, an RFC 2132 client-id, DUID-LLT/DUID-LL, and an RFC
///     4361 client-id wrapping a DUID), refusing to look up a randomized address, and choosing
///     between the embedded snapshot and a persisted download at startup.
/// </summary>
public sealed class MacVendorLookupServiceTests : IDisposable
{
    /// <summary>A throwaway persisted-data path for this test class.</summary>
    private readonly string _dataFilePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-mac-vendor-lookup-tests-{Guid.NewGuid():N}.snapshot.br"
    );

    /// <summary>
    ///     This method builds a lookup service reading its persisted data from this test's own path.
    /// </summary>
    private MacVendorLookupService CreateService() =>
        new(
            Options.Create(new MacVendorLookupOptions { DataFilePath = _dataFilePath }),
            NullLogger<MacVendorLookupService>.Instance
        );

    /// <inheritdoc />
    public void Dispose() => File.Delete(_dataFilePath);

    [Theory]
    // A plain chaddr-style MAC, as ARP and DHCPv4 leases store it.
    [InlineData("b8:27:eb:12:34:56", InternetProtocolVersionEnum.IPv4)]
    // RFC 2132 client-identifier: hardware type 1, then the MAC.
    [InlineData("01:b8:27:eb:12:34:56", InternetProtocolVersionEnum.IPv4)]
    // RFC 4361 client-identifier: type 255, a 4-octet IAID, then a DUID-LL.
    [InlineData("ff:00:00:00:01:00:03:00:01:b8:27:eb:12:34:56", InternetProtocolVersionEnum.IPv4)]
    // DUID-LL (type 3, hardware type 1).
    [InlineData("00:03:00:01:b8:27:eb:12:34:56", InternetProtocolVersionEnum.IPv6)]
    // DUID-LLT (type 1, hardware type 1, 4-octet time).
    [InlineData("00:01:00:01:2c:1f:00:00:b8:27:eb:12:34:56", InternetProtocolVersionEnum.IPv6)]
    public void LookupClientIdentifier_ExtractsTheEmbeddedMacFromEveryIdentifierShape(
        string identifier,
        InternetProtocolVersionEnum protocolVersion
    )
    {
        MacVendorLookupResult result = CreateService()
            .LookupClientIdentifier(identifier, protocolVersion);

        Assert.Equal("b8:27:eb:12:34:56", result.MacAddress);
        Assert.Contains("Raspberry Pi", result.Vendor);
        Assert.False(result.IsRandomized);
    }

    [Theory]
    // DUID-EN (type 2) carries an enterprise number, not a MAC.
    [InlineData("00:02:00:00:ab:11:01:02:03:04", InternetProtocolVersionEnum.IPv6)]
    // A free-form identifier an operator typed.
    [InlineData("living-room-tv", InternetProtocolVersionEnum.IPv4)]
    [InlineData("", InternetProtocolVersionEnum.IPv4)]
    public void LookupClientIdentifier_ReportsNoMacWhenTheIdentifierEmbedsNone(
        string identifier,
        InternetProtocolVersionEnum protocolVersion
    )
    {
        MacVendorLookupResult result = CreateService()
            .LookupClientIdentifier(identifier, protocolVersion);

        Assert.Null(result.MacAddress);
        Assert.Null(result.Vendor);
        Assert.False(result.IsRandomized);
    }

    [Theory]
    [InlineData("ba:27:eb:12:34:56")] // Raspberry Pi's block with the U/L bit set
    [InlineData("5e:e0:00:12:34:56")]
    [InlineData("02:00:00:00:00:01")]
    public void LookupClientIdentifier_NeverLooksUpALocallyAdministeredAddress(string identifier)
    {
        MacVendorLookupResult result = CreateService()
            .LookupClientIdentifier(identifier, InternetProtocolVersionEnum.IPv4);

        Assert.Equal(identifier, result.MacAddress);
        Assert.Null(result.Vendor);
        Assert.True(result.IsRandomized);
    }

    [Fact]
    public void Constructor_PrefersAPersistedDownloadNewerThanTheEmbeddedSnapshot()
    {
        MacVendorTable embedded = MacVendorLookupService.LoadEmbeddedSnapshot();
        MacVendorTable newer = new(
            [new(MacVendorRegistryEnum.MaL, 0xB827EB, "Newer Download Holder")],
            embedded.GeneratedAtUtc.AddDays(1),
            MacVendorDataSourceEnum.Downloaded
        );
        using (FileStream stream = File.Create(_dataFilePath))
            MacVendorSnapshotCodec.Write(stream, newer);

        MacVendorLookupService service = CreateService();

        Assert.Equal(MacVendorDataSourceEnum.Downloaded, service.Current.Source);
        Assert.Equal(
            "Newer Download Holder",
            service
                .LookupClientIdentifier("b8:27:eb:00:00:01", InternetProtocolVersionEnum.IPv4)
                .Vendor
        );
    }

    [Fact]
    public void Constructor_KeepsTheEmbeddedSnapshotOverAnOlderPersistedDownload()
    {
        MacVendorTable embedded = MacVendorLookupService.LoadEmbeddedSnapshot();
        MacVendorTable older = new(
            [new(MacVendorRegistryEnum.MaL, 0xB827EB, "Older Download Holder")],
            embedded.GeneratedAtUtc.AddDays(-1),
            MacVendorDataSourceEnum.Downloaded
        );
        using (FileStream stream = File.Create(_dataFilePath))
            MacVendorSnapshotCodec.Write(stream, older);

        Assert.Equal(MacVendorDataSourceEnum.Embedded, CreateService().Current.Source);
    }

    [Fact]
    public void Constructor_IgnoresACorruptPersistedFile()
    {
        File.WriteAllText(_dataFilePath, "definitely not brotli");

        MacVendorLookupService service = CreateService();

        Assert.Equal(MacVendorDataSourceEnum.Embedded, service.Current.Source);
        Assert.True(service.Current.TotalCount > 0);
    }
}
