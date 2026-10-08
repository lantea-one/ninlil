using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.IO;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Backup.Catalog;
using Ninlil.Dns.Server.Backup.Crypto;
using Ninlil.Dns.Server.Backup.Model;
using Ninlil.Dns.Server.Backup.Options;
using Ninlil.Dns.Server.Core.Certificates;

// Define our namespace.
namespace Ninlil.Dns.Server.Backup;

/// <summary>
///     This class implements <see cref="IBackupService" />; <c>BackupController</c> and
///     <c>StorageEngineSwitchService</c> in <c>Ninlil.Web</c> use it. A backup is a ZIP container
///     (a manifest, one JSON file per <see cref="BackupEntityCatalog" /> entry, the identity
///     relationships, and copies of the Data Protection key ring, the TLS certificate and the
///     runtime configuration file). The finished ZIP is then encrypted as one blob with
///     <see cref="BackupArchiveCodec.Encrypt" /> when a passphrase is given, or only given a header
///     with <see cref="BackupArchiveCodec.Wrap" /> when it is not; an unencrypted archive is
///     clearly marked rather than encrypted with a weak default key. Encrypting the whole ZIP once
///     means one passphrase and one authentication tag, and the ZIP is already compressed.
///     <para>
///         A restore first decrypts or unwraps the archive (as its header says), checks the
///         manifest, and reads and validates every entry in memory. A wrong passphrase, a
///         tampered archive, a missing entry, an unexpected key ring entry, a second
///         certificate or configuration entry, a configuration that is not a JSON object or
///         more decompressed data than <see cref="BackupOptions.MaximumRestoredPayloadBytes" />
///         is rejected before anything is written, and so is entity JSON that does not parse.
///         The bundled files are then staged next to their
///         targets. The existing rows are wiped and the archive's rows imported inside one
///         database transaction (<see cref="IUnitOfWorkTransactionFactory" />), and only after
///         that transaction commits are the staged files renamed into place, so a failure rolls
///         the database back and leaves every file as it was. The Data Protection key ring is
///         merged: the archive's keys are added and no existing key is deleted. On a storage
///         engine without a transaction factory (MongoDB for now) the restore logs a warning and
///         is not atomic: a database error after the wipe leaves a partly restored database,
///         though no file has been replaced.
///         Only one restore runs at a time; a second one is refused with
///         <see cref="BackupRestoreInProgressException" />.
///     </para>
/// </summary>
/// <param name="scopeFactory">Used to open one dependency injection scope per backup/restore.</param>
/// <param name="backupOptions">The key ring directory, runtime configuration file and minimum passphrase length.</param>
/// <param name="tlsCertificateOptions">Where the shared TLS certificate lives.</param>
/// <param name="storageOptions">The active storage engine, recorded in the manifest for reference.</param>
/// <param name="logger">The logger this service records progress and failures to.</param>
public sealed partial class SystemBackupService(
    IServiceScopeFactory scopeFactory,
    IOptions<BackupOptions> backupOptions,
    TlsCertificateOptions tlsCertificateOptions,
    StorageOptions storageOptions,
    ILogger<SystemBackupService> logger
) : IBackupService
{
    /// <summary>
    ///     This constant contains the version of the payload layout (entities and files) this
    ///     class writes and can import. It changes only when the layout changes in a way an older
    ///     importer could not read.
    /// </summary>
    public const int CurrentPayloadFormatVersion = 1;

    private const string ManifestEntryName = "manifest.json";
    private const string IdentityRelationshipsEntryName = "database/identity-relationships.json";
    private const string KeyRingEntryPrefix = "keyring/";
    private const string CertificateEntryPrefix = "certificate/";
    private const string ConfigurationEntryPrefix = "config/";

    /// <summary>
    ///     This field is the restore gate. The service is scoped, so the gate is static to cover
    ///     every request in the process.
    /// </summary>
    private static readonly SemaphoreSlim RestoreGate = new(1, 1);

    private static readonly JsonDocumentOptions ConfigurationJsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    ///     This method matches the file name ASP.NET Core Data Protection gives a key,
    ///     <c>key-&lt;guid&gt;.xml</c>. It is the only name a restore writes into the key ring
    ///     directory, which also holds the database and other secrets.
    /// </summary>
    [GeneratedRegex(
        @"^key-[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}\.xml\z",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex KeyRingFileNamePattern();

    /// <inheritdoc />
    public async Task<byte[]> CreateBackupAsync(
        string passphrase,
        CancellationToken stoppingToken = default
    )
    {
        // A null or blank passphrase means the operator chose no encryption, which is not an
        // error. Only a passphrase that is given but too short is rejected.
        bool encrypt = !string.IsNullOrWhiteSpace(passphrase);
        if (encrypt)
            ValidatePassphrase(passphrase);

        using IServiceScope scope = scopeFactory.CreateScope();
        using MemoryStream zipStream = new();

        Dictionary<string, int> entityCounts = new(StringComparer.OrdinalIgnoreCase);

        using (ZipArchive archive = new(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Export order does not matter (only import and wipe order does); the catalog order
            // gives a deterministic archive layout.
            foreach (BackupEntityDescriptor descriptor in BackupEntityCatalog.Entries)
            {
                string json = await descriptor.ExportJsonAsync(
                    scope.ServiceProvider,
                    stoppingToken
                );
                entityCounts[descriptor.Name] = CountJsonArrayElements(json);
                WriteJsonEntry(archive, $"database/{descriptor.Name}.json", json);
            }

            string identityRelationshipsJson = await ExportIdentityRelationshipsAsync(
                scope.ServiceProvider,
                stoppingToken
            );
            WriteJsonEntry(archive, IdentityRelationshipsEntryName, identityRelationshipsJson);

            foreach (string keyFilePath in ListKeyRingFiles())
                WriteFileEntry(
                    archive,
                    $"{KeyRingEntryPrefix}{Path.GetFileName(keyFilePath)}",
                    keyFilePath
                );

            string certificatePath = ResolveTlsCertificatePath();
            if (File.Exists(certificatePath))
                WriteFileEntry(
                    archive,
                    $"{CertificateEntryPrefix}{Path.GetFileName(certificatePath)}",
                    certificatePath
                );

            string configurationPath = backupOptions.Value.RuntimeConfigurationFilePath;
            if (!string.IsNullOrWhiteSpace(configurationPath) && File.Exists(configurationPath))
                WriteFileEntry(
                    archive,
                    $"{ConfigurationEntryPrefix}{Path.GetFileName(configurationPath)}",
                    configurationPath
                );

            BackupManifest manifest = new(
                CurrentPayloadFormatVersion,
                DateTimeOffset.UtcNow,
                storageOptions.Engine.ToString(),
                entityCounts
            );
            WriteJsonEntry(
                archive,
                ManifestEntryName,
                Ninlil.Common.Serializer.JsonSerializer.Serialize(manifest)
            );
        }

        logger.LogInformation(
            "Created a{EncryptedNote} backup archive covering {EntityTypeCount} entity types and {TotalEntities} total rows.",
            encrypt ? "n encrypted" : "n UNENCRYPTED",
            entityCounts.Count,
            entityCounts.Values.Sum()
        );

        byte[] payload = zipStream.ToArray();
        return encrypt
            ? BackupArchiveCodec.Encrypt(payload, passphrase)
            : BackupArchiveCodec.Wrap(payload);
    }

    /// <inheritdoc />
    public async Task<BackupRestoreSummary> RestoreBackupAsync(
        Stream archiveStream,
        string passphrase,
        CancellationToken stoppingToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(archiveStream);

        // A second restore is refused, not queued: it would be working from an archive chosen
        // before the first one changed the data.
        if (!RestoreGate.Wait(0, CancellationToken.None))
            throw new BackupRestoreInProgressException();

        try
        {
            return await RestoreCoreAsync(archiveStream, passphrase, stoppingToken);
        }
        finally
        {
            RestoreGate.Release();
        }
    }

    /// <summary>
    ///     This method does the restore once <see cref="RestoreGate" /> is held: decrypt, validate
    ///     and read everything, stage the files, change the database, then commit the files.
    /// </summary>
    private async Task<BackupRestoreSummary> RestoreCoreAsync(
        Stream archiveStream,
        string passphrase,
        CancellationToken stoppingToken
    )
    {
        using MemoryStream buffer = new();
        await archiveStream.CopyToAsync(buffer, stoppingToken);
        byte[] archiveBytes = buffer.ToArray();

        // The archive header, not whether a passphrase was supplied, decides how the file is
        // read: a passphrase given for an unencrypted archive is ignored, and a missing one for
        // an encrypted archive is reported as such.
        byte[] plaintext = BackupArchiveCodec.PeekKind(archiveBytes) switch
        {
            BackupArchiveKind.Unencrypted => BackupArchiveCodec.Unwrap(archiveBytes),
            BackupArchiveKind.Encrypted => DecryptRequiringPassphrase(archiveBytes, passphrase),
            _ => throw new BackupArchiveInvalidException(
                "This file does not look like a Ninlil backup archive."
            ),
        };

        // The reads below are in memory only; nothing is written until they have all succeeded.
        using MemoryStream zipStream = new(plaintext);
        using ZipArchive archive = OpenArchiveOrThrow(zipStream);
        RestoreReadBudget budget = new(backupOptions.Value.MaximumRestoredPayloadBytes);

        BackupManifest manifest = ReadManifest(archive, budget);
        if (manifest.FormatVersion != CurrentPayloadFormatVersion)
            throw new BackupArchiveInvalidException(
                $"This archive was produced by a format version ({manifest.FormatVersion}) this "
                    + $"server does not know how to import (it knows version {CurrentPayloadFormatVersion})."
            );

        Dictionary<string, string> entityJsonByName = new(StringComparer.OrdinalIgnoreCase);
        foreach (BackupEntityDescriptor descriptor in BackupEntityCatalog.Entries)
            entityJsonByName[descriptor.Name] = ReadRequiredEntryText(
                archive,
                $"database/{descriptor.Name}.json",
                budget
            );

        string identityRelationshipsJson = ReadRequiredEntryText(
            archive,
            IdentityRelationshipsEntryName,
            budget
        );

        RestoredFiles files = ReadAndValidateFiles(archive, budget);

        // Every entity file is parsed now, before anything is wiped, so malformed JSON aborts the
        // restore with nothing changed.
        Dictionary<string, IReadOnlyList<EntityBase>> parsedByName = new(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (BackupEntityDescriptor descriptor in BackupEntityCatalog.Entries)
            parsedByName[descriptor.Name] = ParseOrThrow(
                $"database/{descriptor.Name}.json",
                () => descriptor.Parse(entityJsonByName[descriptor.Name])
            );

        BackupIdentityRelationships identityRelationships = ParseOrThrow(
            IdentityRelationshipsEntryName,
            () =>
            {
                BackupIdentityRelationships bundle =
                    Ninlil.Common.Serializer.JsonSerializer.Deserialize<BackupIdentityRelationships>(
                        identityRelationshipsJson
                    ) ?? new BackupIdentityRelationships([], [], []);

                if (
                    bundle.UserRoles is null
                    || bundle.UserClaims is null
                    || bundle.RoleClaims is null
                    || bundle.UserRoles.Any(item => item is null)
                    || bundle.UserClaims.Any(item => item is null)
                    || bundle.RoleClaims.Any(item => item is null)
                )
                    throw new JsonException("A list is missing or holds a null element.");

                return bundle;
            }
        );

        // Every required entry is present and valid. The files are staged next to their targets
        // first, so a full disk or a permission error aborts here with nothing changed; the
        // database is changed next, in one transaction where the engine supports it; the files
        // are renamed into place last, only after that transaction commits.
        string configurationPath = backupOptions.Value.RuntimeConfigurationFilePath;
        bool restoresConfiguration =
            files.Configuration is not null && !string.IsNullOrWhiteSpace(configurationPath);
        SemaphoreSlim configurationLock = restoresConfiguration
            ? AtomicFile.GetPathLock(configurationPath)
            : null;
        List<(string Path, StagedFile File)> stagedFiles = [];
        bool configurationLocked = false;

        try
        {
            if (configurationLock is not null)
            {
                // Held until the end, so a settings save cannot slip between the staging and the
                // rename and be lost.
                await configurationLock.WaitAsync(stoppingToken);
                configurationLocked = true;
            }

            StageFiles(files, restoresConfiguration, stagedFiles);

            using IServiceScope scope = scopeFactory.CreateScope();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            // Optional: the relational engines register it. Without it (MongoDB for now) the
            // restore still works but a failure after the wipe leaves a partly restored database.
            IUnitOfWorkTransactionFactory transactionFactory =
                scope.ServiceProvider.GetService<IUnitOfWorkTransactionFactory>();
            if (transactionFactory is null)
                logger.LogWarning(
                    "The storage engine has no transaction support for restores, so this restore "
                        + "is not atomic: a failure after the existing data is removed leaves a "
                        + "partly restored database."
                );

            // Declared after the scope so it is disposed (rolled back, if not committed) first.
            // A null resource is allowed.
            await using IUnitOfWorkTransaction transaction = transactionFactory is null
                ? null
                : await transactionFactory.BeginAsync(stoppingToken);

            // Role memberships and claims are removed before users and roles, so no relationship is
            // left pointing at a deleted row (see WipeIdentityRelationshipsAsync).
            await WipeIdentityRelationshipsAsync(scope.ServiceProvider, stoppingToken);
            await unitOfWork.SaveChangesAsync(stoppingToken);

            // Entity types are wiped in reverse catalog order, so a referencing row is removed before
            // the row it references and no foreign key is violated, without relying on cascades
            // (see BackupEntityCatalog).
            foreach (BackupEntityDescriptor descriptor in BackupEntityCatalog.Entries.Reverse())
            {
                await descriptor.WipeAsync(scope.ServiceProvider, stoppingToken);
                await unitOfWork.SaveChangesAsync(stoppingToken);
            }

            // Entity types are imported in catalog order, so a referenced row (a zone) exists before
            // the rows that reference it (its records).
            Dictionary<string, int> importedCounts = new(StringComparer.OrdinalIgnoreCase);
            foreach (BackupEntityDescriptor descriptor in BackupEntityCatalog.Entries)
            {
                int imported = await descriptor.ImportAsync(
                    scope.ServiceProvider,
                    parsedByName[descriptor.Name],
                    stoppingToken
                );
                importedCounts[descriptor.Name] = imported;
                await unitOfWork.SaveChangesAsync(stoppingToken);
            }

            // Identity relationships are imported last, once every user and role exists again with
            // its original identifier.
            await ImportIdentityRelationshipsAsync(
                scope.ServiceProvider,
                identityRelationships,
                stoppingToken
            );
            await unitOfWork.SaveChangesAsync(stoppingToken);

            // The point of no return. Cancellation is not observed here: once the commit has
            // started, the caller must not be told the restore failed.
            if (transaction is not null)
                await transaction.CommitAsync(CancellationToken.None);

            // Only now are the files replaced: a rollback above leaves the disk untouched.
            CommitStagedFiles(stagedFiles);

            logger.LogInformation(
                "Restored a backup archive covering {EntityTypeCount} entity types and {TotalEntities} total rows.",
                importedCounts.Count,
                importedCounts.Values.Sum()
            );

            return new BackupRestoreSummary(importedCounts.Values.Sum(), importedCounts);
        }
        finally
        {
            // A staged file that was committed ignores Dispose; one that was not is deleted.
            foreach ((string _, StagedFile file) in stagedFiles)
                file.Dispose();

            if (configurationLocked)
                configurationLock.Release();
        }
    }

    /// <summary>
    ///     This method runs <paramref name="parse" /> and turns a <see cref="JsonException" /> into
    ///     a <see cref="BackupArchiveInvalidException" /> naming the archive entry, so malformed
    ///     data is reported as a bad archive (before anything is changed) and not as a server error.
    /// </summary>
    private static T ParseOrThrow<T>(string entryName, Func<T> parse)
    {
        try
        {
            return parse();
        }
        catch (JsonException exception)
        {
            throw new BackupArchiveInvalidException(
                $"The archive entry '{entryName}' is not valid: {exception.Message}"
            );
        }
    }

    /// <summary>
    ///     This method rejects a passphrase shorter than
    ///     <see cref="BackupOptions.MinimumPassphraseLength" />. <see cref="CreateBackupAsync" />
    ///     calls it only when a passphrase was given. A short passphrase would make the archive
    ///     easy to brute-force whatever the cipher.
    /// </summary>
    private void ValidatePassphrase(string passphrase)
    {
        if (
            string.IsNullOrWhiteSpace(passphrase)
            || passphrase.Length < backupOptions.Value.MinimumPassphraseLength
        )
            throw new ArgumentException(
                $"A backup passphrase must be at least {backupOptions.Value.MinimumPassphraseLength} characters long.",
                nameof(passphrase)
            );
    }

    /// <summary>
    ///     This method decrypts an archive that <see cref="BackupArchiveCodec.PeekKind" /> found to
    ///     be encrypted. A missing passphrase is rejected with its own message instead of the
    ///     generic wrong-passphrase-or-tampered one from <see cref="BackupArchiveCodec.Decrypt" />.
    ///     <see cref="BackupOptions.MinimumPassphraseLength" /> is not checked here: the archive
    ///     may have been made under a lower minimum, and restoring only needs the passphrase it
    ///     was encrypted with.
    /// </summary>
    private static byte[] DecryptRequiringPassphrase(byte[] archiveBytes, string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
            throw new ArgumentException(
                "This archive is encrypted. Supply the passphrase it was created with to restore it.",
                nameof(passphrase)
            );

        return BackupArchiveCodec.Decrypt(archiveBytes, passphrase);
    }

    /// <summary>
    ///     This method returns the shared TLS certificate's source file: the operator's PFX when
    ///     <see cref="TlsCertificateOptions.PfxPath" /> is set, otherwise the self-signed state
    ///     file. It follows the same rule as <c>ReloadableTlsCertificateProvider</c>, so a backup
    ///     holds the certificate the TLS transports use.
    /// </summary>
    private string ResolveTlsCertificatePath() =>
        string.IsNullOrWhiteSpace(tlsCertificateOptions.PfxPath)
            ? tlsCertificateOptions.SelfSignedCertificateStatePath
            : tlsCertificateOptions.PfxPath;

    /// <summary>
    ///     This method lists every Data Protection key ring file in
    ///     <see cref="BackupOptions.DataProtectionKeyRingDirectory" />, matched by the ASP.NET Core
    ///     <c>key-&lt;guid&gt;.xml</c> naming pattern, the same pattern a restore requires. The same
    ///     directory also holds, by default, the SQLite database and the JWT signing key, which
    ///     must not be picked up.
    /// </summary>
    private IReadOnlyList<string> ListKeyRingFiles()
    {
        string directory = backupOptions.Value.DataProtectionKeyRingDirectory;
        return Directory.Exists(directory)
            ?
            [
                .. Directory
                    .GetFiles(directory, "key-*.xml")
                    .Where(path => KeyRingFileNamePattern().IsMatch(Path.GetFileName(path))),
            ]
            : [];
    }

    /// <summary>
    ///     This method counts the top-level elements of a JSON array without deserializing them,
    ///     for <see cref="BackupManifest.EntityCounts" />.
    /// </summary>
    private static int CountJsonArrayElements(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return 0;

        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.GetArrayLength()
            : 0;
    }

    /// <summary>
    ///     This method reads every user-role membership, user claim and role claim. The catalog
    ///     cannot cover them because none of the three has an <see cref="IRepository{TEntity}" />;
    ///     they are read through the user and role repositories instead.
    /// </summary>
    private static async Task<string> ExportIdentityRelationshipsAsync(
        IServiceProvider services,
        CancellationToken stoppingToken
    )
    {
        IUserRepository userRepository = services.GetRequiredService<IUserRepository>();
        IRoleRepository roleRepository = services.GetRequiredService<IRoleRepository>();

        IReadOnlyList<UserEntity> users = await userRepository.ListAsync(
            stoppingToken: stoppingToken
        );
        IReadOnlyList<RoleEntity> roles = await roleRepository.ListAsync(
            stoppingToken: stoppingToken
        );

        List<BackupUserRoleAssignment> userRoles = [];
        List<BackupClaimAssignment> userClaims = [];
        foreach (UserEntity user in users)
        {
            foreach (RoleEntity role in await userRepository.ListRolesAsync(user.Id, stoppingToken))
                userRoles.Add(new BackupUserRoleAssignment(user.Id, role.Id));

            foreach (
                UserClaimEntity claim in await userRepository.ListClaimsAsync(
                    user.Id,
                    stoppingToken
                )
            )
                userClaims.Add(
                    new BackupClaimAssignment(user.Id, claim.ClaimType, claim.ClaimValue)
                );
        }

        List<BackupClaimAssignment> roleClaims = [];
        foreach (RoleEntity role in roles)
        foreach (
            RoleClaimEntity claim in await roleRepository.ListClaimsAsync(role.Id, stoppingToken)
        )
            roleClaims.Add(new BackupClaimAssignment(role.Id, claim.ClaimType, claim.ClaimValue));

        return Ninlil.Common.Serializer.JsonSerializer.Serialize(
            new BackupIdentityRelationships(userRoles, userClaims, roleClaims)
        );
    }

    /// <summary>
    ///     This method removes every user-role membership, user claim and role claim before the
    ///     users and roles are wiped through <see cref="BackupEntityCatalog" />. It does not rely
    ///     on cascading deletes, which MongoDB does not have; dependent rows are always removed
    ///     explicitly, as for a deleted zone's records.
    /// </summary>
    private static async Task WipeIdentityRelationshipsAsync(
        IServiceProvider services,
        CancellationToken stoppingToken
    )
    {
        IUserRepository userRepository = services.GetRequiredService<IUserRepository>();
        IRoleRepository roleRepository = services.GetRequiredService<IRoleRepository>();

        foreach (UserEntity user in await userRepository.ListAsync(stoppingToken: stoppingToken))
        {
            foreach (RoleEntity role in await userRepository.ListRolesAsync(user.Id, stoppingToken))
                await userRepository.RemoveFromRoleAsync(user.Id, role.Id, stoppingToken);

            IReadOnlyList<UserClaimEntity> claims = await userRepository.ListClaimsAsync(
                user.Id,
                stoppingToken
            );
            if (claims.Count > 0)
                await userRepository.RemoveClaimsAsync(
                    user.Id,
                    claims.Select(claim => (claim.ClaimType, claim.ClaimValue)),
                    stoppingToken
                );
        }

        foreach (RoleEntity role in await roleRepository.ListAsync(stoppingToken: stoppingToken))
        foreach (
            RoleClaimEntity claim in await roleRepository.ListClaimsAsync(role.Id, stoppingToken)
        )
            await roleRepository.RemoveClaimAsync(
                role.Id,
                claim.ClaimType,
                claim.ClaimValue,
                stoppingToken
            );
    }

    /// <summary>
    ///     This method recreates every user-role membership, user claim and role claim in the
    ///     archive, after the users and roles have been imported with their original identifiers.
    /// </summary>
    private static async Task ImportIdentityRelationshipsAsync(
        IServiceProvider services,
        BackupIdentityRelationships bundle,
        CancellationToken stoppingToken
    )
    {
        IUserRepository userRepository = services.GetRequiredService<IUserRepository>();
        IRoleRepository roleRepository = services.GetRequiredService<IRoleRepository>();

        foreach (BackupUserRoleAssignment assignment in bundle.UserRoles)
            await userRepository.AddToRoleAsync(
                assignment.UserId,
                assignment.RoleId,
                stoppingToken
            );

        foreach (BackupClaimAssignment claim in bundle.UserClaims)
            await userRepository.AddClaimsAsync(
                claim.SubjectId,
                [(claim.ClaimType, claim.ClaimValue)],
                stoppingToken
            );

        foreach (BackupClaimAssignment claim in bundle.RoleClaims)
            await roleRepository.AddClaimAsync(
                claim.SubjectId,
                claim.ClaimType,
                claim.ClaimValue,
                stoppingToken
            );
    }

    /// <summary>
    ///     This method walks the archive's <c>keyring/</c>, <c>certificate/</c> and <c>config/</c>
    ///     entries, reads them within <paramref name="budget" /> and rejects anything that does not
    ///     fit the fixed layout a backup writes. Key ring entries must be named exactly
    ///     <c>keyring/key-&lt;guid&gt;.xml</c> (the name is never taken from the entry's own
    ///     file-name part, so a crafted entry cannot reach the database or another secret in the
    ///     same directory) and hold an XML document whose root is <c>key</c>. There is at most one
    ///     certificate and one configuration entry, directly under their folder; the configuration
    ///     must be a JSON object, so a restore cannot leave a file the next start cannot read. The
    ///     certificate is not opened, because its password may come from the restored configuration.
    ///     Directory entries (a zero-length name ending in a slash) are ignored.
    /// </summary>
    private static RestoredFiles ReadAndValidateFiles(ZipArchive archive, RestoreReadBudget budget)
    {
        Dictionary<string, byte[]> keyRingFiles = new(StringComparer.OrdinalIgnoreCase);
        byte[] certificate = null;
        byte[] configuration = null;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string fullName = entry.FullName;
            bool isDirectory = fullName.EndsWith('/') && entry.Length == 0;

            if (fullName.StartsWith(KeyRingEntryPrefix, StringComparison.Ordinal))
            {
                if (isDirectory)
                    continue;

                string fileName = fullName[KeyRingEntryPrefix.Length..];
                if (!KeyRingFileNamePattern().IsMatch(fileName))
                    throw new BackupArchiveInvalidException(
                        $"This archive has an unexpected key ring entry '{fullName}'."
                    );

                if (keyRingFiles.ContainsKey(fileName))
                    throw new BackupArchiveInvalidException(
                        $"This archive has the key ring entry '{fullName}' more than once."
                    );

                byte[] bytes = budget.Read(entry);
                if (!IsKeyXml(bytes))
                    throw new BackupArchiveInvalidException(
                        $"The key ring entry '{fullName}' is not a Data Protection key file."
                    );

                keyRingFiles[fileName] = bytes;
            }
            else if (fullName.StartsWith(CertificateEntryPrefix, StringComparison.Ordinal))
            {
                if (isDirectory)
                    continue;

                RequireDirectChild(fullName, CertificateEntryPrefix);
                if (certificate is not null)
                    throw new BackupArchiveInvalidException(
                        "This archive has more than one 'certificate/' entry."
                    );

                certificate = budget.Read(entry);
                if (certificate.Length == 0)
                    throw new BackupArchiveInvalidException(
                        $"The certificate entry '{fullName}' is empty."
                    );
            }
            else if (fullName.StartsWith(ConfigurationEntryPrefix, StringComparison.Ordinal))
            {
                if (isDirectory)
                    continue;

                RequireDirectChild(fullName, ConfigurationEntryPrefix);
                if (configuration is not null)
                    throw new BackupArchiveInvalidException(
                        "This archive has more than one 'config/' entry."
                    );

                configuration = budget.Read(entry);
                if (!IsJsonObject(configuration))
                    throw new BackupArchiveInvalidException(
                        $"The configuration entry '{fullName}' is not a JSON object."
                    );
            }
        }

        return new RestoredFiles(
            [.. keyRingFiles.Select(pair => (pair.Key, pair.Value))],
            certificate,
            configuration
        );
    }

    /// <summary>
    ///     This method rejects an entry that is not directly under <paramref name="prefix" />, or
    ///     has no file name.
    /// </summary>
    private static void RequireDirectChild(string fullName, string prefix)
    {
        string rest = fullName[prefix.Length..];
        if (rest.Length == 0 || rest.Contains('/'))
            throw new BackupArchiveInvalidException(
                $"This archive has an unexpected entry '{fullName}'."
            );
    }

    /// <summary>
    ///     This method returns whether <paramref name="bytes" /> is a well-formed XML document with
    ///     a <c>key</c> root, as every Data Protection key file is. DTDs and external resources are
    ///     refused, so reading the file cannot reach outside the archive.
    /// </summary>
    private static bool IsKeyXml(byte[] bytes)
    {
        try
        {
            using MemoryStream stream = new(bytes);
            using XmlReader reader = XmlReader.Create(
                stream,
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }
            );

            if (reader.MoveToContent() != XmlNodeType.Element || reader.Name != "key")
                return false;

            // Reading to the end checks the whole document is well-formed.
            while (reader.Read()) { }
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>
    ///     This method returns whether <paramref name="bytes" /> is a JSON object, read with the
    ///     options the configuration applier uses (comments and trailing commas allowed).
    /// </summary>
    private static bool IsJsonObject(byte[] bytes)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, ConfigurationJsonOptions);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    ///     This method writes every file the restore will replace to a temporary file next to its
    ///     target, in the order they are committed: key ring, certificate, configuration. Key ring
    ///     and certificate are owner-only; the configuration keeps its existing mode. The caller
    ///     holds the configuration path lock. Any failure leaves every target unchanged, and the
    ///     caller deletes the temporary files already staged.
    /// </summary>
    private void StageFiles(
        RestoredFiles files,
        bool restoresConfiguration,
        List<(string Path, StagedFile File)> stagedFiles
    )
    {
        string keyRingDirectory = backupOptions.Value.DataProtectionKeyRingDirectory;
        foreach ((string fileName, byte[] bytes) in files.KeyRing)
        {
            string path = Path.Combine(keyRingDirectory, fileName);
            stagedFiles.Add((path, AtomicFile.Stage(path, bytes, AtomicFilePermissions.OwnerOnly)));
        }

        if (files.Certificate is not null)
        {
            string path = ResolveTlsCertificatePath();
            stagedFiles.Add(
                (path, AtomicFile.Stage(path, files.Certificate, AtomicFilePermissions.OwnerOnly))
            );
        }

        if (restoresConfiguration)
        {
            string path = backupOptions.Value.RuntimeConfigurationFilePath;
            stagedFiles.Add(
                (
                    path,
                    AtomicFile.Stage(
                        path,
                        files.Configuration,
                        AtomicFilePermissions.PreserveExisting
                    )
                )
            );
        }
    }

    /// <summary>
    ///     This method renames the staged files into place. A failure is rare (a rename inside one
    ///     directory), but the database is already restored then, so the log names the files
    ///     already replaced and those still pending before the exception is rethrown.
    /// </summary>
    private void CommitStagedFiles(List<(string Path, StagedFile File)> stagedFiles)
    {
        List<string> done = [];
        foreach ((string path, StagedFile file) in stagedFiles)
        {
            try
            {
                file.Commit();
                done.Add(path);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "The restore replaced the database but failed to replace '{FailedFile}'. "
                        + "Replaced files: [{DoneFiles}]. Files not replaced: [{PendingFiles}].",
                    path,
                    string.Join(", ", done),
                    string.Join(
                        ", ",
                        stagedFiles.Select(item => item.Path).Except(done).Where(p => p != path)
                    )
                );
                throw;
            }
        }
    }

    /// <summary>
    ///     This record holds the validated file payload of an archive: the key ring files as
    ///     (<c>key-&lt;guid&gt;.xml</c> name, content), and the certificate and configuration
    ///     content, or null when the archive has none.
    /// </summary>
    private sealed record RestoredFiles(
        IReadOnlyList<(string FileName, byte[] Bytes)> KeyRing,
        byte[] Certificate,
        byte[] Configuration
    );

    /// <summary>
    ///     This class counts the decompressed bytes of one restore against the cap. It counts what
    ///     is actually produced while an entry is read, never the size the ZIP claims, so a zip
    ///     bomb is stopped after at most the cap plus one buffer.
    /// </summary>
    private sealed class RestoreReadBudget
    {
        private readonly long _maximumBytes;
        private long _remaining;

        public RestoreReadBudget(long maximumBytes)
        {
            _maximumBytes = maximumBytes;
            _remaining = maximumBytes;
        }

        /// <summary>
        ///     This method reads one entry completely, failing as soon as the budget (or the
        ///     largest array .NET can hold) is exceeded.
        /// </summary>
        public byte[] Read(ZipArchiveEntry entry)
        {
            using Stream entryStream = entry.Open();
            using MemoryStream output = new();
            byte[] chunk = new byte[81920];
            int read;
            while ((read = entryStream.Read(chunk, 0, chunk.Length)) > 0)
            {
                _remaining -= read;
                if (_remaining < 0 || output.Length + read > Array.MaxLength)
                    throw new BackupArchiveInvalidException(
                        "This archive holds more data than a restore accepts "
                            + $"({_maximumBytes:N0} bytes when unpacked)."
                    );

                output.Write(chunk, 0, read);
            }

            return output.ToArray();
        }
    }

    /// <summary>
    ///     This method opens <paramref name="zipStream" /> as a <see cref="ZipArchive" />,
    ///     turning the <see cref="InvalidDataException" /> of a malformed ZIP into
    ///     <see cref="BackupArchiveInvalidException" />. Decryption has already succeeded here, so
    ///     the content is simply not a Ninlil backup.
    /// </summary>
    private static ZipArchive OpenArchiveOrThrow(Stream zipStream)
    {
        try
        {
            return new ZipArchive(zipStream, ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            throw new BackupArchiveInvalidException(
                "This archive decrypted successfully but does not contain a valid Ninlil backup."
            );
        }
    }

    /// <summary>
    ///     This method reads and parses <see cref="ManifestEntryName" />, translating either a
    ///     missing entry or malformed JSON into <see cref="BackupArchiveInvalidException" />.
    /// </summary>
    private static BackupManifest ReadManifest(ZipArchive archive, RestoreReadBudget budget)
    {
        string json = ReadRequiredEntryText(archive, ManifestEntryName, budget);

        try
        {
            return Ninlil.Common.Serializer.JsonSerializer.Deserialize<BackupManifest>(json)
                ?? throw new BackupArchiveInvalidException(
                    "This archive's own manifest could not be read."
                );
        }
        catch (JsonException)
        {
            throw new BackupArchiveInvalidException(
                "This archive's own manifest is not valid JSON."
            );
        }
    }

    /// <summary>
    ///     This method reads the text of one ZIP entry within <paramref name="budget" />, throwing
    ///     <see cref="BackupArchiveInvalidException" /> if the entry is missing or the budget is used up.
    /// </summary>
    private static string ReadRequiredEntryText(
        ZipArchive archive,
        string entryName,
        RestoreReadBudget budget
    )
    {
        ZipArchiveEntry entry =
            archive.GetEntry(entryName)
            ?? throw new BackupArchiveInvalidException(
                $"This archive is missing its own required '{entryName}' entry."
            );

        // The bytes are counted against the cap first; the reader then drops a UTF-8 byte order
        // mark as before.
        using MemoryStream bytes = new(budget.Read(entry));
        using StreamReader reader = new(bytes, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    ///     This method writes a JSON string as one ZIP entry.
    /// </summary>
    private static void WriteJsonEntry(ZipArchive archive, string entryName, string json)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using Stream entryStream = entry.Open();
        using StreamWriter writer = new(entryStream, Encoding.UTF8);
        writer.Write(json);
    }

    /// <summary>
    ///     This method copies an existing file on disk into the archive as one ZIP entry.
    /// </summary>
    private static void WriteFileEntry(ZipArchive archive, string entryName, string sourceFilePath)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using Stream entryStream = entry.Open();
        using FileStream sourceStream = File.OpenRead(sourceFilePath);
        sourceStream.CopyTo(entryStream);
    }
}
