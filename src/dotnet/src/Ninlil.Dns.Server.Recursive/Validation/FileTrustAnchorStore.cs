using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Common.Serializer;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This class implements <see cref="ITrustAnchorStore" /> as a single JSON file on disk,
///     following the same "small local file for state that genuinely cannot live in the
///     database" convention already used elsewhere (the SQLite bootstrap store's own file,
///     under an app-relative <c>data</c> directory) -- a full database dependency would be
///     disproportionate for tracking a handful of trust anchor candidates, and this resolver has
///     no other reason to depend on <c>Ninlil.Dns.Data.Abstractions</c> at all.
/// </summary>
/// <param name="options">This store's own configured file path and initial seed.</param>
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
    ///     This method loads this store's state file, or seeds a brand-new one from this
    ///     resolver's own configured (or hardcoded) initial trust anchors if the file does not
    ///     exist yet, or cannot be read at all.
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
    ///     This method writes this store's state to disk, creating its containing directory if
    ///     it does not exist yet.
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
            // A failed write leaves the in-memory state (already updated by the caller) as the
            // only copy until the next successful write -- correct behavior keeps working for
            // the rest of this process's lifetime either way, so this is a warning, not fatal.
            logger.LogWarning(
                exception,
                "Failed to persist the trust anchor state file at '{Path}'.",
                path
            );
        }
    }
}
