using System.Collections.Concurrent;

// Define our namespace.
namespace Ninlil.Dns.Common.IO;

/// <summary>
///     This enum says which file mode <see cref="AtomicFile" /> gives the file it writes.
/// </summary>
public enum AtomicFilePermissions
{
    /// <summary>
    ///     The file is readable and writable by its owner only (mode 0600), even if the target
    ///     already exists with a wider mode. Used for key material.
    /// </summary>
    OwnerOnly,

    /// <summary>
    ///     The file keeps the mode of the existing target, or gets 0600 when there is none, so an
    ///     operator's choice (for example 0640 for a group that reads the file) survives a rewrite.
    /// </summary>
    PreserveExisting,
}

/// <summary>
///     This class writes a file so that a crash or a failed write never leaves it half-written.
///     The content goes to a temporary file in the same directory, is flushed to disk and is then
///     renamed over the target, which is atomic on one filesystem. It is the single writer for
///     key material and configuration files (the TLS certificate, the trust-anchor state, the JWT
///     signing key, the configuration file, and the ACME, DNSCrypt and backup files), so every one
///     of them gets the same mode handling instead of its own copy of the write-then-chmod
///     pattern, which briefly leaves a world-readable file.
///     <para>
///         On Unix the temporary file is created with the final mode and the mode is set again on
///         the open handle, so a restrictive umask cannot strip bits and the content is never
///         readable by other users. On Windows no ACL is set: the file inherits the ACL of its
///         directory, and an existing target keeps its own. Windows is not a shipped platform.
///     </para>
///     <para>
///         Limits: the directory is not fsynced after the rename, because .NET cannot do that
///         portably, so a power cut right after a write can still lose the rename. The owner of
///         the rewritten file is the process user, not the previous owner.
///     </para>
///     <para>
///         <see cref="GetPathLock" /> gives one lock per file path for read-modify-write cycles.
///         The write methods do not take it themselves, so a caller can hold it around its read.
///     </para>
/// </summary>
public static class AtomicFile
{
    private const UnixFileMode OwnerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const UnixFileMode OwnerOnlyDirectory =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PathLocks = new(
        // The file systems of Windows and macOS are case-insensitive by default.
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal
    );

    /// <summary>
    ///     This method returns the lock that serialises writers of one file. Two spellings of the
    ///     same path (<c>a/../a/f</c> and <c>a/f</c>) share one lock. The lock is never removed,
    ///     because the set of configuration and key files is small and fixed.
    /// </summary>
    /// <param name="path">The file path.</param>
    public static SemaphoreSlim GetPathLock(string path) =>
        PathLocks.GetOrAdd(Path.GetFullPath(path), static _ => new SemaphoreSlim(1, 1));

    /// <summary>
    ///     This method writes <paramref name="content" /> to a temporary file next to
    ///     <paramref name="path" /> and returns a handle whose <see cref="StagedFile.Commit" />
    ///     renames it into place. A caller that must validate several files before replacing any
    ///     of them stages them all first and commits afterwards. The target is not touched until
    ///     <see cref="StagedFile.Commit" />.
    /// </summary>
    /// <param name="path">The target file.</param>
    /// <param name="content">The complete new content.</param>
    /// <param name="permissions">Which mode the file gets.</param>
    public static StagedFile Stage(
        string path,
        ReadOnlySpan<byte> content,
        AtomicFilePermissions permissions
    )
    {
        FileStream stream = CreateTemp(path, permissions, out string fullPath, out string tempPath);

        try
        {
            using (stream)
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            return new StagedFile(tempPath, fullPath);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    ///     This method stages <paramref name="content" /> and commits it at once.
    /// </summary>
    /// <inheritdoc cref="Stage" />
    public static void WriteAllBytes(
        string path,
        ReadOnlySpan<byte> content,
        AtomicFilePermissions permissions
    )
    {
        using StagedFile staged = Stage(path, content, permissions);
        staged.Commit();
    }

    /// <summary>
    ///     This method is the asynchronous form of <see cref="WriteAllBytes" />.
    /// </summary>
    /// <inheritdoc cref="Stage" />
    public static async Task WriteAllBytesAsync(
        string path,
        ReadOnlyMemory<byte> content,
        AtomicFilePermissions permissions,
        CancellationToken stoppingToken = default
    )
    {
        FileStream stream = CreateTemp(path, permissions, out string fullPath, out string tempPath);
        StagedFile staged = null;

        try
        {
            await using (stream)
            {
                await stream.WriteAsync(content, stoppingToken);
                stream.Flush(flushToDisk: true);
            }

            staged = new StagedFile(tempPath, fullPath);
            staged.Commit();
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
        finally
        {
            staged?.Dispose();
        }
    }

    /// <summary>
    ///     This method creates the directory if needed and opens a new temporary file in it with
    ///     the mode the final file will have.
    /// </summary>
    private static FileStream CreateTemp(
        string path,
        AtomicFilePermissions permissions,
        out string fullPath,
        out string tempPath
    )
    {
        fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath);

        if (!Directory.Exists(directory))
        {
            if (permissions == AtomicFilePermissions.OwnerOnly && !OperatingSystem.IsWindows())
                Directory.CreateDirectory(directory, OwnerOnlyDirectory);
            else
                Directory.CreateDirectory(directory);
        }

        UnixFileMode mode = OwnerReadWrite;

        // Only a regular existing file lends its mode; a directory at the target path makes the
        // later rename fail, and the caller sees that error.
        if (
            permissions == AtomicFilePermissions.PreserveExisting
            && !OperatingSystem.IsWindows()
            && File.Exists(fullPath)
        )
            mode = File.GetUnixFileMode(fullPath);

        // The name never ends in ".xml", so the Data Protection key ring never reads a temp file.
        tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        FileStreamOptions fileOptions = new()
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };

        if (!OperatingSystem.IsWindows())
            fileOptions.UnixCreateMode = mode;

        FileStream stream = new(tempPath, fileOptions);

        try
        {
            // The umask applies to the create call above, so set the mode again on the handle.
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(stream.SafeFileHandle, mode);

            return stream;
        }
        catch
        {
            stream.Dispose();
            TryDelete(tempPath);
            throw;
        }
    }

    internal static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done for a leftover temp file; the original error is the one
            // worth reporting.
        }
    }
}
