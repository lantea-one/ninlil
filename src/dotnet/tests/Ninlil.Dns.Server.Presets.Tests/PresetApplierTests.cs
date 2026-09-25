using System.Text.Json.Nodes;

// Define our namespace.
namespace Ninlil.Dns.Server.Presets.Tests;

/// <summary>
///     This class tests <see cref="PresetApplier" /> end to end against a JSON file in a
///     temporary directory and the in-memory repositories in <c>InMemoryRepositories.cs</c>.
/// </summary>
public sealed class PresetApplierTests : IDisposable
{
    private readonly string _tempDirectory = Directory
        .CreateTempSubdirectory("ninlil-preset-applier-tests-")
        .FullName;

    private string ConfigPath => Path.Combine(_tempDirectory, "appsettings.json");

    public void Dispose() => Directory.Delete(_tempDirectory, recursive: true);

    [Fact]
    public async Task ApplyingFamilyWritesConfigurationAndCreatesTheFilterListsInOneCall()
    {
        InMemoryDnsClientGroupRepository clientGroups = new();
        InMemoryDnsFilterListRepository filterLists = new();
        InMemoryDnsClientGroupListAssignmentRepository assignments = new();
        InMemoryUnitOfWork unitOfWork = new();
        PresetFilterListApplier filterListApplier = new(
            clientGroups,
            filterLists,
            assignments,
            unitOfWork
        );
        PresetApplier applier = new(filterListApplier);

        await applier.ApplyAsync(DnsServerPresetCatalog.Family, ConfigPath);

        JsonObject root = (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(ConfigPath));
        Assert.True(root["DnsServer"]["Filtering"]["Enable"].GetValue<bool>());

        Assert.Single(clientGroups.Items);
        Assert.Equal(2, filterLists.Items.Count);
        Assert.Equal(2, assignments.Items.Count);
    }
}
