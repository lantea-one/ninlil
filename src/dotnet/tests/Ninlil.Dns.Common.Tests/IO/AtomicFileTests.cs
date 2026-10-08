using Ninlil.Dns.Common.IO;

// Define our namespace.
namespace Ninlil.Dns.Common.Tests.IO;

/// <summary>
///     This class tests <see cref="AtomicFile" /> against real files in a temporary directory:
///     content, Unix file modes (checked with <see cref="File.GetUnixFileMode(string)" />, so the
///     mode tests return early on Windows), cleanup of temporary files and the per-path lock.
/// </summary>
public sealed class AtomicFileTests : IDisposable
{
    private readonly string _directory = Directory
        .CreateTempSubdirectory("ninlil-atomicfile-tests-")
        .FullName;

    private string TargetPath => Path.Combine(_directory, "target.bin");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string[] TempFiles() =>
        [.. Directory.GetFiles(_directory, "*.tmp", SearchOption.AllDirectories)];

    [Fact]
    public void WrittenContentRoundTripsAndReplacesAnExistingFile()
    {
        File.WriteAllBytes(TargetPath, [9, 9, 9, 9, 9]);

        AtomicFile.WriteAllBytes(TargetPath, [1, 2, 3], AtomicFilePermissions.OwnerOnly);

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(TargetPath));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public async Task AsyncWriteRoundTripsAndCreatesMissingDirectories()
    {
        string nested = Path.Combine(_directory, "a", "b", "file.bin");

        await AtomicFile.WriteAllBytesAsync(
            nested,
            new byte[] { 4, 5 },
            AtomicFilePermissions.OwnerOnly
        );

        Assert.Equal(new byte[] { 4, 5 }, await File.ReadAllBytesAsync(nested));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void ANewOwnerOnlyFileIsMode0600()
    {
        if (OperatingSystem.IsWindows())
            return;

        AtomicFile.WriteAllBytes(TargetPath, [1], AtomicFilePermissions.OwnerOnly);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(TargetPath)
        );
    }

    [Fact]
    public void OwnerOnlyNarrowsAnExistingWiderFile()
    {
        if (OperatingSystem.IsWindows())
            return;

        File.WriteAllBytes(TargetPath, [1]);
        File.SetUnixFileMode(
            TargetPath,
            UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.GroupRead
                | UnixFileMode.OtherRead
        );

        AtomicFile.WriteAllBytes(TargetPath, [2], AtomicFilePermissions.OwnerOnly);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(TargetPath)
        );
    }

    [Fact]
    public void PreserveExistingKeepsAGroupReadableMode()
    {
        if (OperatingSystem.IsWindows())
            return;

        UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
        File.WriteAllBytes(TargetPath, [1]);
        File.SetUnixFileMode(TargetPath, mode);

        AtomicFile.WriteAllBytes(TargetPath, [2], AtomicFilePermissions.PreserveExisting);

        Assert.Equal(mode, File.GetUnixFileMode(TargetPath));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(TargetPath));
    }

    [Fact]
    public void PreserveExistingGives0600ToAFileThatDoesNotExistYet()
    {
        if (OperatingSystem.IsWindows())
            return;

        AtomicFile.WriteAllBytes(TargetPath, [1], AtomicFilePermissions.PreserveExisting);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(TargetPath)
        );
    }

    [Fact]
    public void AMissingDirectoryIsCreatedOwnerOnlyForOwnerOnlyFiles()
    {
        if (OperatingSystem.IsWindows())
            return;

        string nested = Path.Combine(_directory, "secret", "file.bin");

        AtomicFile.WriteAllBytes(nested, [1], AtomicFilePermissions.OwnerOnly);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(Path.GetDirectoryName(nested))
        );
    }

    [Fact]
    public void ADisposedUncommittedStageLeavesTheTargetUnchangedAndNoTempFile()
    {
        File.WriteAllBytes(TargetPath, [7]);

        using (
            StagedFile staged = AtomicFile.Stage(
                TargetPath,
                [1, 2],
                AtomicFilePermissions.OwnerOnly
            )
        )
            Assert.Single(TempFiles());

        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(TargetPath));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void TheTargetIsUntouchedUntilCommit()
    {
        File.WriteAllBytes(TargetPath, [7]);

        using StagedFile staged = AtomicFile.Stage(
            TargetPath,
            [1, 2],
            AtomicFilePermissions.OwnerOnly
        );
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(TargetPath));

        staged.Commit();

        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(TargetPath));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void AFailedCommitLeavesTheTargetIntactAndRemovesTheTempFile()
    {
        // A directory at the target path makes the rename fail.
        Directory.CreateDirectory(TargetPath);

        Assert.ThrowsAny<Exception>(() =>
            AtomicFile.WriteAllBytes(TargetPath, [1], AtomicFilePermissions.OwnerOnly)
        );

        Assert.True(Directory.Exists(TargetPath));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void ThePathLockIsTheSameInstanceForTwoSpellingsOfOnePath()
    {
        string plain = Path.Combine(_directory, "a", "f");
        string roundabout = Path.Combine(_directory, "a", "..", "a", "f");

        Assert.Same(AtomicFile.GetPathLock(plain), AtomicFile.GetPathLock(roundabout));
        Assert.NotSame(
            AtomicFile.GetPathLock(plain),
            AtomicFile.GetPathLock(Path.Combine(_directory, "a", "g"))
        );
    }
}
