// Define our namespace.
namespace Ninlil.Dns.Server.Presets.Tests;

public class DnsServerPresetCatalogTests
{
    [Fact]
    public void AllContainsExactlyTheFiveConfirmedPresets()
    {
        Assert.Equal(
            ["Open", "Baseline", "Protective", "Family", "FIPS"],
            DnsServerPresetCatalog.All.Select(preset => preset.Name)
        );
    }

    [Theory]
    [InlineData("family")]
    [InlineData("FAMILY")]
    [InlineData("Family")]
    public void TryGetByNameIsCaseInsensitive(string name)
    {
        Assert.True(DnsServerPresetCatalog.TryGetByName(name, out PresetDefinition preset));
        Assert.Same(DnsServerPresetCatalog.Family, preset);
    }

    [Fact]
    public void TryGetByNameFailsForAnUnknownName()
    {
        Assert.False(
            DnsServerPresetCatalog.TryGetByName("Nonexistent", out PresetDefinition preset)
        );
        Assert.Null(preset);
    }

    [Fact]
    public void ProtectiveLayersOnTopOfEveryBaselineSetting()
    {
        foreach (
            PresetConfigurationSetting setting in DnsServerPresetCatalog
                .Baseline
                .ConfigurationSettings
        )
            Assert.Contains(setting, DnsServerPresetCatalog.Protective.ConfigurationSettings);
    }

    [Fact]
    public void FamilyLayersOnTopOfEveryProtectiveSettingAndSubscription()
    {
        foreach (
            PresetConfigurationSetting setting in DnsServerPresetCatalog
                .Protective
                .ConfigurationSettings
        )
            Assert.Contains(setting, DnsServerPresetCatalog.Family.ConfigurationSettings);

        foreach (
            PresetFilterListSubscription subscription in DnsServerPresetCatalog
                .Protective
                .FilterListSubscriptions
        )
            Assert.Contains(subscription, DnsServerPresetCatalog.Family.FilterListSubscriptions);
    }

    [Fact]
    public void OnlyProtectiveAndFamilyCarryFilterListSubscriptions()
    {
        Assert.Empty(DnsServerPresetCatalog.Open.FilterListSubscriptions);
        Assert.Empty(DnsServerPresetCatalog.Baseline.FilterListSubscriptions);
        Assert.NotEmpty(DnsServerPresetCatalog.Protective.FilterListSubscriptions);
        Assert.NotEmpty(DnsServerPresetCatalog.Family.FilterListSubscriptions);
        Assert.Empty(DnsServerPresetCatalog.Fips.FilterListSubscriptions);
    }

    [Fact]
    public void FamilyHasStrictlyMoreSubscriptionsThanProtective() =>
        Assert.True(
            DnsServerPresetCatalog.Family.FilterListSubscriptions.Count
                > DnsServerPresetCatalog.Protective.FilterListSubscriptions.Count
        );

    [Fact]
    public void EverySubscriptionUrlIsAbsolute()
    {
        foreach (
            PresetFilterListSubscription subscription in DnsServerPresetCatalog.All.SelectMany(
                preset => preset.FilterListSubscriptions
            )
        )
            Assert.True(Uri.IsWellFormedUriString(subscription.SubscriptionUrl, UriKind.Absolute));
    }

    [Fact]
    public void EveryPresetsOwnSubscriptionListHasUniqueNames()
    {
        // Family deliberately reuses Protective's own subscription (same stable name, same
        // list) as part of its own layering -- uniqueness only needs to hold *within* a single
        // preset's own subscription list, not across every preset in the catalog.
        foreach (PresetDefinition preset in DnsServerPresetCatalog.All)
        {
            List<string> names = [.. preset.FilterListSubscriptions.Select(s => s.Name)];
            Assert.Equal(names.Count, names.Distinct().Count());
        }
    }
}
