// Define our namespace.
namespace Ninlil.Dns.Common.IO;

/// <summary>
///     This class is a fully written temporary file waiting to replace its target. Calling
///     <see cref="Commit" /> renames it into place; disposing it without a commit deletes it.
/// </summary>
public sealed class StagedFile : IDisposable
{
    private readonly string _tempPath;
    private readonly string _targetPath;
    private bool _finished;

    internal StagedFile(string tempPath, string targetPath)
    {
        _tempPath = tempPath;
        _targetPath = targetPath;
    }

    /// <summary>
    ///     This method replaces the target with the staged file. On failure the temporary file
    ///     is deleted, the target is unchanged and the exception is rethrown.
    /// </summary>
    public void Commit()
    {
        ObjectDisposedException.ThrowIf(_finished, this);

        try
        {
            if (OperatingSystem.IsWindows() && File.Exists(_targetPath))
                // Replace keeps the ACL of the existing target.
                File.Replace(_tempPath, _targetPath, null, ignoreMetadataErrors: true);
            else
                // rename(2) on Unix: atomic, and it overwrites an existing file.
                File.Move(_tempPath, _targetPath, overwrite: true);
        }
        catch
        {
            AtomicFile.TryDelete(_tempPath);
            throw;
        }
        finally
        {
            _finished = true;
        }
    }

    /// <summary>
    ///     This method deletes the temporary file if <see cref="Commit" /> did not run.
    /// </summary>
    public void Dispose()
    {
        if (_finished)
            return;

        _finished = true;
        AtomicFile.TryDelete(_tempPath);
    }
}
