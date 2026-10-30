// Define our namespace.
namespace Ninlil.Dns.Server.Presets.Tests;

/// <summary>
///     This class checks the built-in presets in <see cref="DnsServerPresetCatalog" />: name
///     lookup, the layering of Baseline, Protective and Family, and the shape of their
///     filter-list subscriptions. It inspects the definitions only; nothing is applied.
/// </summary>
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
    public void FamilyDescription_NamesTheThreeContentCategoriesItsListBlocks()
    {
        // The Family list is a hosts file of fake-news, gambling and adult domains, so the
        // description must not claim adult content alone.
        string description = DnsServerPresetCatalog.Family.Description;

        Assert.Contains("fake-news", description);
        Assert.Contains("gambling", description);
        Assert.Contains("adult", description);
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
        // Family reuses Protective's subscription (same name, same list), so names only need to
        // be unique within one preset, not across the catalog.
        foreach (PresetDefinition preset in DnsServerPresetCatalog.All)
        {
            List<string> names = [.. preset.FilterListSubscriptions.Select(s => s.Name)];
            Assert.Equal(names.Count, names.Distinct().Count());
        }
    }
}
