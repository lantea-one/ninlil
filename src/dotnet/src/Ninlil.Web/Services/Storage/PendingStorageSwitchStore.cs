using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.IO;

// Define our namespace.
namespace Ninlil.Web.Services.Storage;

/// <summary>
///     This record is a storage switch that has been requested and not yet applied.
/// </summary>
/// <param name="SourceEngine">The engine that served the data when the switch was requested.</param>
/// <param name="SourceConnectionString">
///     The source's effective connection string. Null when the record was read without its
///     secrets.
/// </param>
/// <param name="TargetEngine">The engine the data moves to.</param>
/// <param name="TargetConnectionString">
///     The target's connection string. Null when the record was read without its secrets.
/// </param>
/// <param name="RequestedAtUtc">When the switch was requested.</param>
/// <param name="RequestedBy">The user name that requested it.</param>
public sealed record PendingStorageSwitch(
    StorageEngineEnum SourceEngine,
    string SourceConnectionString,
    StorageEngineEnum TargetEngine,
    string TargetConnectionString,
    DateTimeOffset RequestedAtUtc,
    string RequestedBy
);

/// <summary>
///     This class keeps the one pending storage switch in <c>storage-switch.json</c> in the data
///     directory. <see cref="StorageEngineSwitchService" /> saves and cancels it,
///     <see cref="PendingStorageSwitchApplier" /> reads and deletes it at startup, and
///     <c>StorageController</c> shows its non-secret part. Both connection strings are protected
///     with the Data Protection key ring under the purpose
///     <see cref="ProtectorPurpose" />, so the file holds no password in clear text. The file is
///     written atomically with owner-only permissions.
/// </summary>
public sealed class PendingStorageSwitchStore
{
    /// <summary>
    ///     This constant is the Data Protection purpose that protects the connection strings.
    /// </summary>
    public const string ProtectorPurpose = "Ninlil.Storage.PendingSwitch";

    /// <summary>
    ///     This constant is the file name inside the data directory.
    /// </summary>
    public const string FileName = "storage-switch.json";

    private readonly string _path;
    private readonly IDataProtector _protector;
    private readonly ILogger<PendingStorageSwitchStore> _logger;

    /// <summary>
    ///     This constructor sets the store up for one data directory.
    /// </summary>
    /// <param name="dataDirectory">The directory that holds the file.</param>
    /// <param name="dataProtectionProvider">Protects the connection strings.</param>
    /// <param name="logger">Records an unreadable file.</param>
    public PendingStorageSwitchStore(
        string dataDirectory,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<PendingStorageSwitchStore> logger
    )
    {
        _path = Path.Combine(dataDirectory, FileName);
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _logger = logger;
    }

    /// <summary>
    ///     This method saves <paramref name="pending" />, replacing any earlier record.
    /// </summary>
    public async Task SaveAsync(PendingStorageSwitch pending, CancellationToken stoppingToken)
    {
        StoredRecord stored = new()
        {
            SourceEngine = pending.SourceEngine,
            ProtectedSourceConnectionString = _protector.Protect(pending.SourceConnectionString),
            TargetEngine = pending.TargetEngine,
            ProtectedTargetConnectionString = _protector.Protect(pending.TargetConnectionString),
            RequestedAtUtc = pending.RequestedAtUtc,
            RequestedBy = pending.RequestedBy,
        };

        await AtomicFile.WriteAllBytesAsync(
            _path,
            JsonSerializer.SerializeToUtf8Bytes(stored),
            AtomicFilePermissions.OwnerOnly,
            stoppingToken
        );
    }

    /// <summary>
    ///     This method reads the record with its connection strings decrypted. A file that cannot
    ///     be read or decrypted (the key ring was replaced) is logged as an error and deleted,
    ///     and the method returns null, because the record can never be applied.
    /// </summary>
    /// <returns>The record, or null when none is pending or the file is unusable.</returns>
    public PendingStorageSwitch Load()
    {
        StoredRecord stored = ReadFile();
        if (stored is null)
            return null;

        try
        {
            return new PendingStorageSwitch(
                stored.SourceEngine,
                _protector.Unprotect(stored.ProtectedSourceConnectionString),
                stored.TargetEngine,
                _protector.Unprotect(stored.ProtectedTargetConnectionString),
                stored.RequestedAtUtc,
                stored.RequestedBy
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "The pending storage switch in '{Path}' cannot be decrypted, so it is removed. "
                    + "Request the switch again.",
                _path
            );
            Delete();
            return null;
        }
    }

    /// <summary>
    ///     This method reads the record without decrypting it, so the connection strings are
    ///     null. The status API uses it and never fails on a record it cannot decrypt.
    /// </summary>
    /// <returns>The record without secrets, or null when none is pending or the file is unusable.</returns>
    public PendingStorageSwitch Peek()
    {
        StoredRecord stored = ReadFile();
        return stored is null
            ? null
            : new PendingStorageSwitch(
                stored.SourceEngine,
                null,
                stored.TargetEngine,
                null,
                stored.RequestedAtUtc,
                stored.RequestedBy
            );
    }

    /// <summary>
    ///     This method deletes the record.
    /// </summary>
    /// <returns>True when a record existed.</returns>
    public bool Delete()
    {
        if (!File.Exists(_path))
            return false;

        File.Delete(_path);
        return true;
    }

    private StoredRecord ReadFile()
    {
        if (!File.Exists(_path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<StoredRecord>(File.ReadAllBytes(_path));
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            _logger.LogError(
                exception,
                "The pending storage switch file '{Path}' is unreadable.",
                _path
            );
            return null;
        }
    }

    /// <summary>
    ///     This class is the file's JSON shape: the connection strings are protected, everything
    ///     else is plain so the status API can show it without decrypting.
    /// </summary>
    private sealed class StoredRecord
    {
        public StorageEngineEnum SourceEngine { get; set; }
        public string ProtectedSourceConnectionString { get; set; }
        public StorageEngineEnum TargetEngine { get; set; }
        public string ProtectedTargetConnectionString { get; set; }
        public DateTimeOffset RequestedAtUtc { get; set; }
        public string RequestedBy { get; set; }
    }
}
