using Ninlil.Web.Startup;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class tests <see cref="ClusterNodeIdFile" /> against a file in a temporary
///     directory: a missing file gets a new owner-only id, an existing id is returned unchanged,
///     and a blank file stops startup instead of getting a new id.
/// </summary>
public sealed class ClusterNodeIdFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-node-id-tests-{Guid.NewGuid():N}"
    );

    public ClusterNodeIdFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void AMissingFileGetsANewIdThatIsWrittenAndReadBackUnchanged()
    {
        string path = Path.Combine(_directory, "cluster-node-id.txt");

        string created = ClusterNodeIdFile.ReadOrCreate(path);

        Assert.True(Guid.TryParse(created, out _));
        Assert.Equal(created, File.ReadAllText(path));
        Assert.Equal(created, ClusterNodeIdFile.ReadOrCreate(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void ANewIdFileIsOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
            return;

        string path = Path.Combine(_directory, "cluster-node-id.txt");

        ClusterNodeIdFile.ReadOrCreate(path);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public void AnExistingIdIsReturnedTrimmedAndTheFileIsNotRewritten()
    {
        string path = Path.Combine(_directory, "cluster-node-id.txt");
        File.WriteAllText(path, "  node-a\n");

        Assert.Equal("node-a", ClusterNodeIdFile.ReadOrCreate(path));
        Assert.Equal("  node-a\n", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void ABlankFileStopsStartupWithAMessageNamingTheFileAndTheFixAndIsLeftAlone(
        string content
    )
    {
        string path = Path.Combine(_directory, "cluster-node-id.txt");
        File.WriteAllText(path, content);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ClusterNodeIdFile.ReadOrCreate(path)
        );

        Assert.Contains(path, exception.Message);
        Assert.Contains("Restore the original", exception.Message);
        Assert.Contains("never joined a cluster", exception.Message);
        Assert.Equal(content, File.ReadAllText(path));
    }
}
