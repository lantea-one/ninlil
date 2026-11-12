using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.Cookies;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.ClusterSync;
using Ninlil.Web.Configuration;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="ClusterSyncedSettingsCatalog" /> and the guard in
///     <see cref="ClusterConfigSyncSettingsRecorder" /> that refuses an uncatalogued section or
///     key, without a web host. The controller side of the completeness check is
///     <see cref="ClusterSyncedSettingsControllerCatalogTests" />.
/// </summary>
public class ClusterSyncedSettingsCatalogTests
{
    [Fact]
    public async Task RecordAndPublishAsync_ForASectionMissingFromTheCatalog_Throws()
    {
        (
            ClusterConfigSyncSettingsRecorder recorder,
            SettingsClusterSyncRoundTripTests.CapturingPublisher publisher
        ) = CreateRecorder();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            recorder.RecordAndPublishAsync(
                "DnsServer:NotCatalogued",
                [new PresetConfigurationSetting("DnsServer:NotCatalogued", "Enable", true)],
                CancellationToken.None
            )
        );

        Assert.Null(publisher.Published);
    }

    [Fact]
    public async Task RecordAndPublishAsync_ForAKeyMissingFromTheCatalog_Throws()
    {
        (
            ClusterConfigSyncSettingsRecorder recorder,
            SettingsClusterSyncRoundTripTests.CapturingPublisher publisher
        ) = CreateRecorder();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                recorder.RecordAndPublishAsync(
                    DnsCookieOptions.SectionName,
                    [
                        new PresetConfigurationSetting(
                            DnsCookieOptions.SectionName,
                            "Enable",
                            true
                        ),
                        new PresetConfigurationSetting(DnsCookieOptions.SectionName, "NewKey", 1),
                    ],
                    CancellationToken.None
                )
        );

        Assert.Contains("DnsServer:Cookies:NewKey", exception.Message);
        Assert.Null(publisher.Published);
    }

    [Fact]
    public async Task RecordAndPublishAsync_ForAKeyOfTheRightSectionUnderAnotherPath_Throws()
    {
        (ClusterConfigSyncSettingsRecorder recorder, _) = CreateRecorder();

        // "Enable" is catalogued under the cookies path only.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            recorder.RecordAndPublishAsync(
                DnsCookieOptions.SectionName,
                [new PresetConfigurationSetting("DnsServer:Cookies:Other", "Enable", true)],
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task RecordAndPublishAsync_ForCataloguedKeysInAnyCase_Publishes()
    {
        (
            ClusterConfigSyncSettingsRecorder recorder,
            SettingsClusterSyncRoundTripTests.CapturingPublisher publisher
        ) = CreateRecorder();

        // Configuration keys are case-insensitive, so the catalogue match is too.
        await recorder.RecordAndPublishAsync(
            DnsCookieOptions.SectionName,
            [new PresetConfigurationSetting("dnsserver:cookies", "enable", true)],
            CancellationToken.None
        );

        Assert.NotNull(publisher.Published);
    }

    [Fact]
    public async Task ApplyAndRecordAsync_ForAnUncataloguedKey_LeavesTheFileUntouched()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ninlil-catalog-{Guid.NewGuid():N}.json");
        const string original = """{"DnsServer":{"Cookies":{"Enable":true}}}""";
        await File.WriteAllTextAsync(path, original);

        try
        {
            (ClusterConfigSyncSettingsRecorder recorder, _) = CreateRecorder();
            PresetConfigurationSetting[] settings =
            [
                new(DnsCookieOptions.SectionName, "Require", true),
                new(DnsCookieOptions.SectionName, "NewKey", 1),
            ];

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                recorder.ApplyAndRecordAsync(
                    DnsCookieOptions.SectionName,
                    settings,
                    settings,
                    path,
                    CancellationToken.None
                )
            );

            // Neither the catalogued key nor the refused one reached the file.
            Assert.Equal(original, await File.ReadAllTextAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ApplyAndRecordAsync_ForCataloguedKeys_WritesTheFileAndPublishes()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ninlil-catalog-{Guid.NewGuid():N}.json");

        try
        {
            (
                ClusterConfigSyncSettingsRecorder recorder,
                SettingsClusterSyncRoundTripTests.CapturingPublisher publisher
            ) = CreateRecorder();
            PresetConfigurationSetting[] settings =
            [
                new(DnsCookieOptions.SectionName, "Require", true),
            ];

            await recorder.ApplyAndRecordAsync(
                DnsCookieOptions.SectionName,
                settings,
                settings,
                path,
                CancellationToken.None
            );

            Assert.Contains("\"Require\": true", await File.ReadAllTextAsync(path));
            Assert.NotNull(publisher.Published);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Sections_NeverListAnEncryptedSecretKey()
    {
        // Ciphertext opens only with the key ring that made it, so it must never be a synced key.
        foreach (ConfigurationSecretSetting secret in ConfigurationSecretSettings.All)
            Assert.DoesNotContain(
                ClusterSyncedSettingsCatalog.Sections.SelectMany(section => section.Keys),
                key =>
                    string.Equals(
                        key.SectionPath,
                        secret.SectionPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && string.Equals(
                        key.Key,
                        secret.EncryptedKey,
                        StringComparison.OrdinalIgnoreCase
                    )
            );
    }

    [Fact]
    public void Sections_ListThePlainKeyOfEveryCataloguedSecret()
    {
        // A secret that is not catalogued would sync live (the controller passes its plain key)
        // but never backfill.
        foreach (ConfigurationSecretSetting secret in ConfigurationSecretSettings.All)
            Assert.Contains(
                ClusterSyncedSettingsCatalog.Sections.SelectMany(section => section.Keys),
                key =>
                    string.Equals(
                        key.SectionPath,
                        secret.SectionPath,
                        StringComparison.OrdinalIgnoreCase
                    ) && string.Equals(key.Key, secret.PlainKey, StringComparison.OrdinalIgnoreCase)
            );
    }

    [Fact]
    public void Sections_NameEachSectionOnce()
    {
        Assert.Equal(
            ClusterSyncedSettingsCatalog.Sections.Count,
            ClusterSyncedSettingsCatalog
                .Sections.Select(section => section.SectionName)
                .Distinct(StringComparer.Ordinal)
                .Count()
        );
    }

    private static (
        ClusterConfigSyncSettingsRecorder Recorder,
        SettingsClusterSyncRoundTripTests.CapturingPublisher Publisher
    ) CreateRecorder()
    {
        SettingsClusterSyncRoundTripTests.CapturingPublisher publisher = new();

        return (
            new ClusterConfigSyncSettingsRecorder(
                new SettingsClusterSyncRoundTripTests.FakeStateRepository(),
                new SettingsClusterSyncRoundTripTests.FakeUnitOfWork(),
                new SettingsClusterSyncRoundTripTests.FakeClusterOptionsMonitor(
                    new ClusterOptions { NodeId = Guid.NewGuid() }
                ),
                publisher
            ),
            publisher
        );
    }
}
