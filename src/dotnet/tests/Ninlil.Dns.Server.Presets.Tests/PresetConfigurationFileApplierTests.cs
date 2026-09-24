using System.Text.Json.Nodes;

// Define our namespace.
namespace Ninlil.Dns.Server.Presets.Tests;

public sealed class PresetConfigurationFileApplierTests : IDisposable
{
    private readonly string _tempDirectory = Directory
        .CreateTempSubdirectory("ninlil-preset-tests-")
        .FullName;

    private string ConfigPath => Path.Combine(_tempDirectory, "appsettings.json");

    public void Dispose() => Directory.Delete(_tempDirectory, recursive: true);

    private async Task<JsonObject> ReadBackAsync() =>
        (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(ConfigPath));

    [Fact]
    public async Task ApplyingBaselineToANonExistentFileCreatesItWithTheRightValues()
    {
        Assert.False(File.Exists(ConfigPath));

        await PresetConfigurationFileApplier.ApplyAsync(
            DnsServerPresetCatalog.Baseline,
            ConfigPath
        );

        JsonObject root = await ReadBackAsync();

        Assert.True(root["DnsServer"]["RateLimiting"]["Enable"].GetValue<bool>());
        Assert.Equal(20, root["DnsServer"]["RateLimiting"]["QueriesPerSecond"].GetValue<int>());
        Assert.True(root["DnsServer"]["Cookies"]["Enable"].GetValue<bool>());
        Assert.False(root["DnsServer"]["Cookies"]["Require"].GetValue<bool>());
        Assert.False(root["DnsServer"]["Filtering"]["Enable"].GetValue<bool>());
    }

    [Fact]
    public async Task ApplyingAPresetNeverTouchesAnUnrelatedPreExistingSetting()
    {
        await File.WriteAllTextAsync(
            ConfigPath,
            """
            { "DnsServer": { "Recursive": { "MaxReferralDepth": 42 } }, "SomeOtherApp": { "Value": "keep-me" } }
            """
        );

        await PresetConfigurationFileApplier.ApplyAsync(
            DnsServerPresetCatalog.Baseline,
            ConfigPath
        );

        JsonObject root = await ReadBackAsync();

        Assert.Equal(42, root["DnsServer"]["Recursive"]["MaxReferralDepth"].GetValue<int>());
        Assert.Equal("keep-me", root["SomeOtherApp"]["Value"].GetValue<string>());
        // Baseline's own settings were still applied alongside the untouched ones.
        Assert.True(root["DnsServer"]["RateLimiting"]["Enable"].GetValue<bool>());
    }

    [Fact]
    public async Task ApplyingOpenAfterFipsResetsEveryCryptoRestrictionKnob()
    {
        await PresetConfigurationFileApplier.ApplyAsync(DnsServerPresetCatalog.Fips, ConfigPath);
        JsonObject afterFips = await ReadBackAsync();
        Assert.True(afterFips["DnsServer"]["Dnssec"]["AllowedAlgorithms"].AsArray().Count > 0);
        Assert.Equal(
            "Tls12, Tls13",
            afterFips["DnsServer"]
                ["Protocol"]["DnsOverTls"]["Certificate"]["AllowedProtocols"]
                .GetValue<string>()
        );

        await PresetConfigurationFileApplier.ApplyAsync(DnsServerPresetCatalog.Open, ConfigPath);
        JsonObject afterOpen = await ReadBackAsync();

        Assert.Empty(afterOpen["DnsServer"]["Dnssec"]["AllowedAlgorithms"].AsArray());
        Assert.Equal(
            "None",
            afterOpen["DnsServer"]
                ["Protocol"]["DnsOverTls"]["Certificate"]["AllowedProtocols"]
                .GetValue<string>()
        );
        Assert.False(afterOpen["DnsServer"]["RateLimiting"]["Enable"].GetValue<bool>());
    }

    [Fact]
    public async Task ApplyingFipsWritesExactlyTheThreeConfirmedAlgorithmNames()
    {
        await PresetConfigurationFileApplier.ApplyAsync(DnsServerPresetCatalog.Fips, ConfigPath);
        JsonObject root = await ReadBackAsync();

        List<string> algorithms =
        [
            .. root["DnsServer"]
                ["Dnssec"]["AllowedAlgorithms"]
                .AsArray()
                .Select(node => node.GetValue<string>()),
        ];

        Assert.Equal(["RsaSha256", "EcdsaP256Sha256", "EcdsaP384Sha384"], algorithms);
    }

    [Fact]
    public async Task ApplyingASettingWhoseValueIsAListOfStructuredObjectsWritesARealJsonArrayOfObjects()
    {
        // A plain list of scalars (a list of algorithm names, for example) was always supported --
        // this proves the same is true of a list of structured objects (a Router Advertisement's
        // own advertised routes, for example), each one serialized generically rather than via
        // JsonValue.Create's own narrower "known scalar CLR type" support, which throws on an
        // arbitrary reference type at runtime.
        await PresetConfigurationFileApplier.ApplyAsync(
            [
                new PresetConfigurationSetting(
                    "DhcpServer:Ra",
                    "Routes",
                    new[]
                    {
                        new
                        {
                            Prefix = "2001:db8:5::",
                            PrefixLength = 64,
                            Preference = "High",
                        },
                        new
                        {
                            Prefix = "2001:db8:6::",
                            PrefixLength = 48,
                            Preference = "Low",
                        },
                    }
                ),
            ],
            ConfigPath
        );

        JsonObject root = await ReadBackAsync();
        JsonArray routes = (JsonArray)root["DhcpServer"]["Ra"]["Routes"];

        Assert.Equal(2, routes.Count);
        Assert.Equal("2001:db8:5::", routes[0]["Prefix"].GetValue<string>());
        Assert.Equal(64, routes[0]["PrefixLength"].GetValue<int>());
        Assert.Equal("High", routes[0]["Preference"].GetValue<string>());
        Assert.Equal("2001:db8:6::", routes[1]["Prefix"].GetValue<string>());
        Assert.Equal(48, routes[1]["PrefixLength"].GetValue<int>());
        Assert.Equal("Low", routes[1]["Preference"].GetValue<string>());
    }

    [Fact]
    public async Task ReapplyingTheSamePresetTwiceProducesTheSameResult()
    {
        await PresetConfigurationFileApplier.ApplyAsync(DnsServerPresetCatalog.Family, ConfigPath);
        string firstApply = await File.ReadAllTextAsync(ConfigPath);

        await PresetConfigurationFileApplier.ApplyAsync(DnsServerPresetCatalog.Family, ConfigPath);
        string secondApply = await File.ReadAllTextAsync(ConfigPath);

        Assert.Equal(firstApply, secondApply);
    }
}
