using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Common.Serializer;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This class implements <see cref="ITrustAnchorStore" /> as one JSON file at
///     <see cref="RecursiveServerOptions.TrustAnchorStateFilePath" />, kept in memory behind a
///     lock. A file keeps this project free of a database dependency for a handful of keys. The
///     file is loaded once, when the store is created; if it is missing or empty, the store is
///     seeded from <see cref="RootTrustAnchors.Resolve" /> and written. Once the file exists,
///     <see cref="RecursiveServerOptions.TrustAnchorsOverride" /> has no effect.
/// </summary>
/// <param name="options">The state file path and the initial seed.</param>
/// <param name="logger">The logger this store records a corrupt or unreadable state file to.</param>
public sealed class FileTrustAnchorStore(
    IOptions<RecursiveServerOptions> options,
    ILogger<FileTrustAnchorStore> logger
) : ITrustAnchorStore
{
    private readonly Lock _lock = new();
    private List<PersistedTrustAnchor> _anchors = LoadOrSeed(options.Value, logger);

    /// <inheritdoc />
    public IReadOnlyList<TrustAnchor> GetValidAnchors()
    {
        lock (_lock)
            return
            [
                .. _anchors
                    .Where(anchor => anchor.State == TrustAnchorStateEnum.Valid)
                    .Select(anchor => new TrustAnchor(
                        anchor.KeyTag,
                        anchor.Algorithm,
                        anchor.DigestType,
                        anchor.Digest
                    )),
            ];
    }

    /// <inheritdoc />
    public IReadOnlyList<PersistedTrustAnchor> GetAll()
    {
        lock (_lock)
            return [.. _anchors];
    }

    /// <inheritdoc />
    public void Save(IReadOnlyList<PersistedTrustAnchor> anchors)
    {
        lock (_lock)
        {
            _anchors = [.. anchors];
            WriteFile(options.Value.TrustAnchorStateFilePath, _anchors, logger);
        }
    }

    /// <summary>
    ///     This method loads the state file, or seeds a new one from the configured (or built-in)
    ///     trust anchors if the file does not exist, is empty, or throws an
    ///     <see cref="IOException" /> or <see cref="FormatException" /> while loading. Other
    ///     exceptions, such as a JSON parse error, propagate.
    /// </summary>
    private static List<PersistedTrustAnchor> LoadOrSeed(
        RecursiveServerOptions options,
        ILogger logger
    )
    {
        string path = options.TrustAnchorStateFilePath;

        if (File.Exists(path))
        {
            try
            {
                List<PersistedTrustAnchor> loaded = JsonSerializer.Deserialize<
                    List<PersistedTrustAnchor>
                >(File.ReadAllText(path));

                if (loaded is { Count: > 0 })
                    return loaded;
            }
            catch (Exception exception) when (exception is IOException or FormatException)
            {
                logger.LogWarning(
                    exception,
                    "The trust anchor state file at '{Path}' could not be read; re-seeding from this resolver's own configured initial trust anchors.",
                    path
                );
            }
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<PersistedTrustAnchor> seeded =
        [
            .. RootTrustAnchors
                .Resolve(options)
                .Select(anchor => new PersistedTrustAnchor(
                    anchor.KeyTag,
                    anchor.Algorithm,
                    anchor.DigestType,
                    anchor.Digest,
                    TrustAnchorStateEnum.Valid,
                    now,
                    null
                )),
        ];

        WriteFile(path, seeded, logger);

        return seeded;
    }

    /// <summary>
    ///     This method writes the state file, creating its directory if needed.
    /// </summary>
    private static void WriteFile(
        string path,
        IReadOnlyList<PersistedTrustAnchor> anchors,
        ILogger logger
    )
    {
        try
        {
            string directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, JsonSerializer.SerializePretty(anchors));
        }
        catch (IOException exception)
        {
            // The in-memory state stays correct for this process, so a failed write is only a
            // warning; the change is lost at restart unless a later write succeeds.
            logger.LogWarning(
                exception,
                "Failed to persist the trust anchor state file at '{Path}'.",
                path
            );
        }
    }
}
