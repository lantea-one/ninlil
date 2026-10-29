using System.Reflection;

// Define our namespace.
namespace Ninlil.TestSupport;

/// <summary>
///     Removes the temporary files and directories that tests create under
///     <see cref="Path.GetTempPath" />. The file is linked into every test project by
///     <c>src/dotnet/tests/Directory.Build.props</c>, so each test class calls it from
///     <c>Dispose</c> or a <c>finally</c> block instead of repeating its own cleanup.
/// </summary>
/// <remarks>
///     SQLite keeps a <c>-wal</c> and a <c>-shm</c> file next to a database in WAL mode and a
///     <c>-journal</c> file while a transaction runs. Deleting only the database file leaves
///     those side files behind, and on a RAM-backed temp directory they add up to gigabytes. A
///     pooled connection also keeps the files open (and blocks the delete on Windows), so the
///     pool of that one database is cleared first. The helper never clears the pools of other
///     databases: tests run in parallel, and a process-wide clear closes connections that other
///     tests are opening or using.
/// </remarks>
internal static class TemporaryFiles
{
    /// <summary>The suffixes SQLite appends to a database path for its side files.</summary>
    private static readonly string[] SqliteSideFileSuffixes = ["-wal", "-shm", "-journal"];

    /// <summary>
    ///     Deletes a SQLite database file together with its <c>-wal</c>, <c>-shm</c> and
    ///     <c>-journal</c> side files. Missing files are ignored. Call it after the context or
    ///     connection that used the database is disposed.
    /// </summary>
    /// <param name="databasePath">The path of the database file.</param>
    public static void DeleteSqliteDatabase(string databasePath)
    {
        if (string.IsNullOrEmpty(databasePath))
            return;

        ClearSqliteConnectionPool(databasePath);

        DeleteFile(databasePath);
        foreach (string suffix in SqliteSideFileSuffixes)
            DeleteFile(databasePath + suffix);
    }

    /// <summary>
    ///     Deletes a directory and everything below it, including SQLite databases with their
    ///     side files. Callers dispose their contexts first; the directory delete does not clear
    ///     connection pools. A missing directory is ignored.
    /// </summary>
    /// <param name="directoryPath">The directory to remove.</param>
    public static void DeleteDirectory(string directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
            return;

        try
        {
            Directory.Delete(directoryPath, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    ///     Deletes one file. A missing file is ignored, and so is a file that another process
    ///     still holds open: cleanup must not turn a passing test into a failing one.
    /// </summary>
    /// <param name="path">The file to remove.</param>
    public static void DeleteFile(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    ///     Closes the pooled SQLite connections for <c>Data Source={databasePath}</c> when the
    ///     SQLite provider is loaded. It goes through reflection because most test projects do
    ///     not reference <c>Microsoft.Data.Sqlite</c>. A pool is keyed by the exact connection
    ///     string, so a test that adds other options to its string keeps its pool; the delete
    ///     still works on Linux, where an open file can be unlinked.
    /// </summary>
    /// <param name="databasePath">The path of the database whose pool is cleared.</param>
    private static void ClearSqliteConnectionPool(string databasePath)
    {
        Type connectionType = Type.GetType(
            "Microsoft.Data.Sqlite.SqliteConnection, Microsoft.Data.Sqlite"
        );
        MethodInfo clearPool = connectionType?.GetMethod(
            "ClearPool",
            BindingFlags.Public | BindingFlags.Static
        );

        if (clearPool is null)
            return;

        using IDisposable connection = (IDisposable)
            Activator.CreateInstance(connectionType, $"Data Source={databasePath}");
        clearPool.Invoke(null, [connection]);
    }
}
