using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.ClientIdentity.Vendor;
using static Ninlil.Dns.Server.ClientIdentity.Tests.Vendor.MacVendorTestSupport;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Tests.Vendor;

/// <summary>
///     This class covers how <see cref="MacVendorRefreshService" /> writes its two files, the
///     persisted table and the status file beside it: after a refresh the directory holds no
///     leftover temporary file, the status file parses, and on Unix an existing file keeps its
///     mode. A scripted HTTP handler serves valid registries; the files live in a throwaway
///     directory. These are guards: atomicity has no behaviour-level red short of a crash in the
///     middle of a write.
/// </summary>
public sealed class MacVendorRefreshStatusFileTests : IDisposable
{
    /// <summary>The throwaway directory holding this test's data and status files.</summary>
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-mac-vendor-status-tests-{Guid.NewGuid():N}"
    );

    /// <summary>The persisted table's path inside <see cref="_directory" />.</summary>
    private string DataFilePath => Path.Combine(_directory, "vendors.snapshot.br");

    /// <summary>The status file's path, which the service derives from the data file's path.</summary>
    private string StatusFilePath => DataFilePath + ".state.json";

    /// <summary>
    ///     This constructor creates the throwaway directory.
    /// </summary>
    public MacVendorRefreshStatusFileTests() => Directory.CreateDirectory(_directory);

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>
    ///     This method builds a refresh service whose handler serves a valid file for each registry.
    /// </summary>
    private MacVendorRefreshService CreateService()
    {
        MacVendorLookupOptions options = new()
        {
            DataFilePath = DataFilePath,
            MaLRegistryUrl = "https://registry.test/oui.csv",
            MaMRegistryUrl = "https://registry.test/mam.csv",
            MaSRegistryUrl = "https://registry.test/oui36.csv",
        };
        ScriptedHandler handler = new(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/oui.csv" => Csv(
                    BuildRegistryCsv(
                        MacVendorRegistryEnum.MaL,
                        MacVendorRefreshService.GetMinimumEntryCount(MacVendorRegistryEnum.MaL),
                        0x100000,
                        "Large Holder"
                    ),
                    "\"large-1\""
                ),
                "/mam.csv" => Csv(
                    BuildRegistryCsv(
                        MacVendorRegistryEnum.MaM,
                        MacVendorRefreshService.GetMinimumEntryCount(MacVendorRegistryEnum.MaM),
                        0x2000000,
                        "Medium Holder"
                    ),
                    "\"medium-1\""
                ),
                _ => Csv(
                    BuildRegistryCsv(
                        MacVendorRegistryEnum.MaS,
                        MacVendorRefreshService.GetMinimumEntryCount(MacVendorRegistryEnum.MaS),
                        0x300000000,
                        "Small Holder"
                    ),
                    "\"small-1\""
                ),
            }
        );

        MacVendorLookupService lookup = new(
            Options.Create(options),
            NullLogger<MacVendorLookupService>.Instance
        );

        // A small loaded table, so the "keep at least half the loaded entries" rule passes.
        lookup.Replace(
            new MacVendorTable(
                [new(MacVendorRegistryEnum.MaL, 0xB827EB, "Initial Holder")],
                DateTimeOffset.UtcNow.AddDays(-30),
                MacVendorDataSourceEnum.Embedded
            )
        );

        return new MacVendorRefreshService(
            new HandlerHttpClientFactory(handler),
            new MutableOptionsMonitor<MacVendorLookupOptions>(options),
            lookup,
            NullLogger<MacVendorRefreshService>.Instance
        );
    }

    [Fact]
    public async Task RefreshNowAsync_AfterTheStatusIsSaved_LeavesNoTemporaryFileAndAParsableStatus()
    {
        MacVendorRefreshStatus result = await CreateService().RefreshNowAsync();

        Assert.Equal(MacVendorRefreshResultEnum.Updated, result.LastResult);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp*"));
        Assert.True(File.Exists(DataFilePath));
        using JsonDocument status = JsonDocument.Parse(File.ReadAllText(StatusFilePath));
        Assert.Equal(JsonValueKind.Object, status.RootElement.ValueKind);
        Assert.Equal(
            [DataFilePath, StatusFilePath],
            Directory.GetFiles(_directory).Order(StringComparer.Ordinal).ToArray()
        );
    }

    [Fact]
    public async Task RefreshNowAsync_WithExistingFilesOfMode0640_KeepsThatMode()
    {
        if (OperatingSystem.IsWindows())
            return;

        const UnixFileMode mode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
        // The service is built first: its lookup loads the data file at construction and would
        // reject this placeholder content.
        MacVendorRefreshService service = CreateService();
        File.WriteAllText(StatusFilePath, "{}");
        File.WriteAllText(DataFilePath, "old");
        File.SetUnixFileMode(StatusFilePath, mode);
        File.SetUnixFileMode(DataFilePath, mode);

        await service.RefreshNowAsync();

        Assert.Equal(mode, File.GetUnixFileMode(StatusFilePath));
        Assert.Equal(mode, File.GetUnixFileMode(DataFilePath));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp*"));
    }
}
