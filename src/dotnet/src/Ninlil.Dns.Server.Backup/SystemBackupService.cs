using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
///         manifest, and reads every required entry and file into memory. A wrong passphrase, a
///         tampered archive or a missing entry is rejected before anything is written. Only then
///         does it wipe and re-import the database and overwrite the bundled files. The entity
///         JSON is parsed during the import, and each entity type is committed separately, so a
///         failure at that stage (malformed entity JSON, a database error) leaves a partly
///         restored database.
///     </para>
/// </summary>
/// <param name="scopeFactory">Used to open one dependency injection scope per backup/restore.</param>
/// <param name="backupOptions">The key ring directory, runtime configuration file and minimum passphrase length.</param>
/// <param name="tlsCertificateOptions">Where the shared TLS certificate lives.</param>
/// <param name="storageOptions">The active storage engine, recorded in the manifest for reference.</param>
/// <param name="logger">The logger this service records progress and failures to.</param>
public sealed class SystemBackupService(
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

        BackupManifest manifest = ReadManifest(archive);
        if (manifest.FormatVersion != CurrentPayloadFormatVersion)
            throw new BackupArchiveInvalidException(
                $"This archive was produced by a format version ({manifest.FormatVersion}) this "
                    + $"server does not know how to import (it knows version {CurrentPayloadFormatVersion})."
            );

        Dictionary<string, string> entityJsonByName = new(StringComparer.OrdinalIgnoreCase);
        foreach (BackupEntityDescriptor descriptor in BackupEntityCatalog.Entries)
            entityJsonByName[descriptor.Name] = ReadRequiredEntryText(
                archive,
                $"database/{descriptor.Name}.json"
            );

        string identityRelationshipsJson = ReadRequiredEntryText(
            archive,
            IdentityRelationshipsEntryName
        );

        List<(string FileName, byte[] Bytes)> keyRingFiles =
        [
            .. archive
                .Entries.Where(entry =>
                    entry.FullName.StartsWith(KeyRingEntryPrefix, StringComparison.Ordinal)
                )
                .Select(entry => (FileName: entry.Name, Bytes: ReadEntryBytes(entry))),
        ];

        List<(string FileName, byte[] Bytes)> certificateCandidates =
        [
            .. archive
                .Entries.Where(entry =>
                    entry.FullName.StartsWith(CertificateEntryPrefix, StringComparison.Ordinal)
                )
                .Select(entry => (FileName: entry.Name, Bytes: ReadEntryBytes(entry))),
        ];
        (string FileName, byte[] Bytes)? certificateFile =
            certificateCandidates.Count > 0 ? certificateCandidates[0] : null;

        List<(string FileName, byte[] Bytes)> configurationCandidates =
        [
            .. archive
                .Entries.Where(entry =>
                    entry.FullName.StartsWith(ConfigurationEntryPrefix, StringComparison.Ordinal)
                )
                .Select(entry => (FileName: entry.Name, Bytes: ReadEntryBytes(entry))),
        ];
        (string FileName, byte[] Bytes)? configurationFile =
            configurationCandidates.Count > 0 ? configurationCandidates[0] : null;

        // Every required entry is present. From here on the database and files are changed.
        using IServiceScope scope = scopeFactory.CreateScope();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

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
            int imported = await descriptor.ImportJsonAsync(
                scope.ServiceProvider,
                entityJsonByName[descriptor.Name],
                stoppingToken
            );
            importedCounts[descriptor.Name] = imported;
            await unitOfWork.SaveChangesAsync(stoppingToken);
        }

        // Identity relationships are imported last, once every user and role exists again with
        // its original identifier.
        await ImportIdentityRelationshipsAsync(
            scope.ServiceProvider,
            identityRelationshipsJson,
            stoppingToken
        );
        await unitOfWork.SaveChangesAsync(stoppingToken);

        RestoreKeyRingFiles(keyRingFiles);
        RestoreCertificateFile(certificateFile);
        RestoreConfigurationFile(configurationFile);

        logger.LogInformation(
            "Restored a backup archive covering {EntityTypeCount} entity types and {TotalEntities} total rows.",
            importedCounts.Count,
            importedCounts.Values.Sum()
        );

        return new BackupRestoreSummary(importedCounts.Values.Sum(), importedCounts);
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
    ///     <c>key-&lt;guid&gt;.xml</c> naming pattern. The same directory also holds, by default,
    ///     the SQLite database and the JWT signing key, which must not be picked up.
    /// </summary>
    private IReadOnlyList<string> ListKeyRingFiles()
    {
        string directory = backupOptions.Value.DataProtectionKeyRingDirectory;
        return Directory.Exists(directory) ? Directory.GetFiles(directory, "key-*.xml") : [];
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
        string json,
        CancellationToken stoppingToken
    )
    {
        BackupIdentityRelationships bundle =
            Ninlil.Common.Serializer.JsonSerializer.Deserialize<BackupIdentityRelationships>(json)
            ?? new BackupIdentityRelationships([], [], []);

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
    ///     This method deletes the existing key ring files (those matching <c>key-*.xml</c>) and
    ///     writes the archive's <c>keyring/</c> entries into the key ring directory, readable by
    ///     the owner only. The entry names are used as they are, without checking the pattern.
    /// </summary>
    private void RestoreKeyRingFiles(IReadOnlyList<(string FileName, byte[] Bytes)> keyRingFiles)
    {
        string directory = backupOptions.Value.DataProtectionKeyRingDirectory;
        Directory.CreateDirectory(directory);

        foreach (string existingFile in ListKeyRingFiles())
            File.Delete(existingFile);

        foreach ((string fileName, byte[] bytes) in keyRingFiles)
        {
            string path = Path.Combine(directory, fileName);
            File.WriteAllBytes(path, bytes);

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <summary>
    ///     This method overwrites the shared TLS certificate's source file (see
    ///     <see cref="ResolveTlsCertificatePath" />) with the archive's copy, if there is one. When
    ///     <see cref="TlsCertificateOptions.PfxPath" /> is set, the copy goes to that path, so the
    ///     server does not switch to the self-signed certificate path.
    /// </summary>
    private void RestoreCertificateFile((string FileName, byte[] Bytes)? certificateFile)
    {
        if (certificateFile is null)
            return;

        string path = ResolveTlsCertificatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, certificateFile.Value.Bytes);

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    /// <summary>
    ///     This method overwrites the runtime configuration file with the archive's copy, when the
    ///     archive has one and a runtime configuration file is configured.
    /// </summary>
    private void RestoreConfigurationFile((string FileName, byte[] Bytes)? configurationFile)
    {
        string path = backupOptions.Value.RuntimeConfigurationFilePath;
        if (configurationFile is null || string.IsNullOrWhiteSpace(path))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, configurationFile.Value.Bytes);
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
    private static BackupManifest ReadManifest(ZipArchive archive)
    {
        string json = ReadRequiredEntryText(archive, ManifestEntryName);

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
    ///     This method reads the text of one ZIP entry, throwing
    ///     <see cref="BackupArchiveInvalidException" /> if the entry is missing.
    /// </summary>
    private static string ReadRequiredEntryText(ZipArchive archive, string entryName)
    {
        ZipArchiveEntry entry =
            archive.GetEntry(entryName)
            ?? throw new BackupArchiveInvalidException(
                $"This archive is missing its own required '{entryName}' entry."
            );

        using Stream entryStream = entry.Open();
        using StreamReader reader = new(entryStream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    ///     This method reads the bytes of one ZIP entry.
    /// </summary>
    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        using Stream entryStream = entry.Open();
        using MemoryStream buffer = new();
        entryStream.CopyTo(buffer);
        return buffer.ToArray();
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
