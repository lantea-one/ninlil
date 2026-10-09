using Microsoft.Extensions.Configuration;
using Ninlil.Web.Services.Storage;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class tests <see cref="StorageConfigurationOrigin" /> with configuration roots built
///     from real JSON files in a temporary directory plus in-memory and environment-variable
///     sources, with no host. It checks which source wins for <c>Storage:Engine</c> and
///     <c>Storage:ConnectionString</c>: the runtime file only when no later source sets either key.
/// </summary>
public sealed class StorageConfigurationOriginTests : IDisposable
{
    private readonly string _directory = Directory
        .CreateDirectory(Path.Combine(Path.GetTempPath(), $"ninlil-origin-{Guid.NewGuid():N}"))
        .FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string WriteFile(string name, string json)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Resolve_WhenOnlyTheRuntimeFileSetsTheEngine_IsTheRuntimeFile()
    {
        string runtime = WriteFile("runtime.json", """{"Storage":{"Engine":"Sqlite"}}""");
        IConfigurationRoot root = new ConfigurationBuilder().AddJsonFile(runtime).Build();

        StorageConfigurationOriginResult origin = StorageConfigurationOrigin.Resolve(root, runtime);

        Assert.True(origin.IsRuntimeFile);
    }

    [Fact]
    public void Resolve_WhenALaterSourceSetsTheEngine_IsExternal()
    {
        string runtime = WriteFile("runtime.json", """{"Storage":{"Engine":"Sqlite"}}""");
        IConfigurationRoot root = new ConfigurationBuilder()
            .AddJsonFile(runtime)
            .AddInMemoryCollection(new Dictionary<string, string> { ["Storage:Engine"] = "MySql" })
            .Build();

        StorageConfigurationOriginResult origin = StorageConfigurationOrigin.Resolve(root, runtime);

        Assert.False(origin.IsRuntimeFile);
    }

    [Fact]
    public void Resolve_WhenAnEnvironmentVariableSetsTheConnectionString_NamesTheEnvironment()
    {
        string runtime = WriteFile("runtime.json", """{"Storage":{"Engine":"Sqlite"}}""");
        string variable = $"NINLILORIGINTEST{Guid.NewGuid():N}_Storage__ConnectionString";
        string prefix = variable[..variable.IndexOf('_')] + "_";
        Environment.SetEnvironmentVariable(variable, "Data Source=x");
        try
        {
            IConfigurationRoot root = new ConfigurationBuilder()
                .AddJsonFile(runtime)
                .AddEnvironmentVariables(prefix)
                .Build();

            StorageConfigurationOriginResult origin = StorageConfigurationOrigin.Resolve(
                root,
                runtime
            );

            Assert.False(origin.IsRuntimeFile);
            Assert.Contains("environment", origin.SourceName, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void Resolve_WhenAnotherJsonFileSetsTheEngine_IsExternalAndNamesTheFile()
    {
        string runtime = WriteFile("runtime.json", "{}");
        string other = WriteFile(
            "appsettings.Production.json",
            """{"Storage":{"Engine":"MySql"}}"""
        );
        IConfigurationRoot root = new ConfigurationBuilder()
            .AddJsonFile(runtime)
            .AddJsonFile(other)
            .Build();

        StorageConfigurationOriginResult origin = StorageConfigurationOrigin.Resolve(root, runtime);

        Assert.False(origin.IsRuntimeFile);
        Assert.Contains("appsettings.Production.json", origin.SourceName, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_WhenNothingSetsStorageAndTheRuntimeFileIsLoaded_IsTheRuntimeFile()
    {
        string runtime = WriteFile("runtime.json", """{"Other":{"Key":"v"}}""");
        IConfigurationRoot root = new ConfigurationBuilder().AddJsonFile(runtime).Build();

        Assert.True(StorageConfigurationOrigin.Resolve(root, runtime).IsRuntimeFile);
    }

    [Fact]
    public void Resolve_WhenNothingSetsStorageAndTheRuntimeFileIsNotASource_IsExternal()
    {
        string runtime = WriteFile("runtime.json", "{}");
        IConfigurationRoot root = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { ["Other:Key"] = "v" })
            .Build();

        Assert.False(StorageConfigurationOrigin.Resolve(root, runtime).IsRuntimeFile);
    }
}
