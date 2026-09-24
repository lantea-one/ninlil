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
///     This class implements <see cref="IBackupService" />. A backup is built as a plaintext ZIP
///     container (a manifest, one JSON file per <see cref="BackupEntityCatalog" /> entry, the
///     identity relationships bundle, and copies of the Data Protection key ring/TLS
///     certificate/runtime configuration file) which is then, as one opaque blob, either encrypted
///     via <see cref="BackupArchiveCodec.Encrypt" /> (a real passphrase was supplied) or merely
///     header-wrapped via <see cref="BackupArchiveCodec.Wrap" /> (none was -- an operator's own
///     deliberate, clearly-marked choice to skip encryption, never a weak or default-derived key
///     standing in for a real one) -- the ZIP container's own compression already shrinks the
///     archive before either path sees it, and treating the finished container as one opaque blob
///     rather than encrypting each piece separately means there is at most one passphrase to
///     remember and at most one authentication tag to verify.
///     <para>
///         A restore always fully reads and parses the entire archive -- decrypting it first only
///         if its own envelope header says it is encrypted -- every entity list, the identity
///         relationships bundle, and the key ring/certificate/configuration file bytes -- into
///         memory before writing a single byte anywhere. Only once every one of those steps has
///         succeeded does this class open one dependency injection scope and actually wipe and
///         re-import the database, then overwrite the three bundled files on disk. This ordering is
///         what lets a caller trust that a rejected restore (a wrong passphrase, a tampered or
///         malformed archive) never leaves the system in a half-restored state.
///     </para>
/// </summary>
/// <param name="scopeFactory">Used to open one dependency injection scope per backup/restore.</param>
/// <param name="backupOptions">Where this server's own key ring lives, and how weak a supplied passphrase may be.</param>
/// <param name="tlsCertificateOptions">Where this server's own shared TLS certificate lives.</param>
/// <param name="storageOptions">Which storage engine is currently active, recorded in the manifest for an operator's own reference.</param>
/// <param name="logger">The logger this service records its own progress and failures to.</param>
public sealed class SystemBackupService(
    IServiceScopeFactory scopeFactory,
    IOptions<BackupOptions> backupOptions,
    TlsCertificateOptions tlsCertificateOptions,
    StorageOptions storageOptions,
    ILogger<SystemBackupService> logger
) : IBackupService
{
    /// <summary>
    ///     This constant contains the version of the decrypted payload's own entity/file layout
    ///     this build of this class produces and knows how to import. Bumped only when that
    ///     layout itself changes in a way an older importer could not safely read.
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
        // A null/blank passphrase is a deliberate operator choice to skip encryption entirely,
        // not an error -- only a genuinely-supplied-but-too-short passphrase is rejected. The
        // length check only ever applies to the encrypted path; there is no minimum length to
        // enforce on the absence of a passphrase.
        bool encrypt = !string.IsNullOrWhiteSpace(passphrase);
        if (encrypt)
            ValidatePassphrase(passphrase);

        using IServiceScope scope = scopeFactory.CreateScope();
        using MemoryStream zipStream = new();

        Dictionary<string, int> entityCounts = new(StringComparer.OrdinalIgnoreCase);

        using (ZipArchive archive = new(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Every entity type this catalog covers is exported in one pass. The order does not
            // matter for export (only import/wipe order is order-sensitive), so the catalog's own
            // forward order is reused here purely for a readable, deterministic archive layout.
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

        // The archive's own magic header, not the caller-supplied passphrase, is the source of
        // truth for which kind this file actually is -- a caller mistakenly supplying a
        // passphrase for an unencrypted archive (or omitting one for an encrypted archive) is
        // handled explicitly below rather than silently misinterpreting the upload.
        byte[] plaintext = BackupArchiveCodec.PeekKind(archiveBytes) switch
        {
            BackupArchiveKind.Unencrypted => BackupArchiveCodec.Unwrap(archiveBytes),
            BackupArchiveKind.Encrypted => DecryptRequiringPassphrase(archiveBytes, passphrase),
            _ => throw new BackupArchiveInvalidException(
                "This file does not look like a Ninlil backup archive."
            ),
        };

        // Everything below reads the decrypted archive into memory only -- nothing is written to
        // this server until every one of these reads and parses has already succeeded.
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

        // Every read above succeeded -- the archive is genuinely well-formed. Only now does this
        // method touch the real database or the real filesystem.
        using IServiceScope scope = scopeFactory.CreateScope();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // What/why: the user-role/claim relationships are wiped first, before either users or
        // roles themselves are touched -- how: removing a role membership or claim while the
        // user/role row it references still exists is always safe, whereas wiping a user or role
        // row while a relationship still references it is exactly the dangling-reference risk
        // neither storage provider's own cascade behavior can be relied on to catch (see
        // WipeIdentityRelationshipsAsync's own doc comment).
        await WipeIdentityRelationshipsAsync(scope.ServiceProvider, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        // What/why/how: entity types are wiped in the exact reverse of BackupEntityCatalog's own
        // forward order (its own doc comment defines that order specifically so this reversal is
        // always safe) -- a row that references another row is always removed before the row it
        // references, avoiding a foreign-key violation on a relational provider even though
        // nothing here actually depends on that provider's own cascade-delete configuration.
        foreach (BackupEntityDescriptor descriptor in BackupEntityCatalog.Entries.Reverse())
        {
            await descriptor.WipeAsync(scope.ServiceProvider, stoppingToken);
            await unitOfWork.SaveChangesAsync(stoppingToken);
        }

        // What/why/how: entity types are re-imported in the catalog's own forward order -- the
        // mirror image of the reverse wipe above -- so a referenced row (a zone, say) always
        // exists again before the rows that reference it (its own records) are imported.
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

        // What/why: identity relationships are re-imported last, only once every user and role row
        // they could possibly reference has already been recreated with its own original
        // identifier intact -- the mirror image of wiping them first, above.
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
    ///     <see cref="BackupOptions.MinimumPassphraseLength" /> when the caller has already chosen
    ///     to encrypt (a blank/null passphrase never reaches this method -- that is the
    ///     unencrypted path instead, decided by <see cref="CreateBackupAsync" /> before this is
    ///     called). A short passphrase would make the archive's own encryption trivially
    ///     brute-forceable regardless of the cipher.
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
    ///     This method decrypts an archive <see cref="RestoreBackupAsync" /> has already
    ///     confirmed (via <see cref="BackupArchiveCodec.PeekKind" />) is the encrypted kind,
    ///     requiring a real, non-blank passphrase first -- restoring an encrypted archive with no
    ///     passphrase at all is rejected here with a message naming exactly what went wrong,
    ///     rather than falling through to <see cref="BackupArchiveCodec.Decrypt" /> and failing
    ///     with the generic wrong-passphrase-or-tampered message. Deliberately does not re-enforce
    ///     <see cref="BackupOptions.MinimumPassphraseLength" /> the way <see cref="ValidatePassphrase" />
    ///     does for a freshly-created archive -- an archive being restored may have been created
    ///     under an earlier, shorter minimum, and the only real requirement to restore one is
    ///     supplying the exact passphrase it was actually encrypted with.
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
    ///     This method resolves the shared TLS certificate's own source file, mirroring
    ///     <c>ReloadableTlsCertificateProvider.ReadSourceFileWriteTimeUtc</c>'s own identical
    ///     "an operator-provided PFX wins over the self-signed state file" logic, so a backup
    ///     always bundles whichever certificate file Kestrel/DoT/DoQ are actually using.
    /// </summary>
    private string ResolveTlsCertificatePath() =>
        string.IsNullOrWhiteSpace(tlsCertificateOptions.PfxPath)
            ? tlsCertificateOptions.SelfSignedCertificateStatePath
            : tlsCertificateOptions.PfxPath;

    /// <summary>
    ///     This method lists every Data Protection key ring file in
    ///     <see cref="BackupOptions.DataProtectionKeyRingDirectory" /> -- matched by ASP.NET
    ///     Core's own <c>key-&lt;guid&gt;.xml</c> naming convention, deliberately narrower than
    ///     "every file in this directory", since that same directory also holds this server's
    ///     own SQLite database file and JWT signing key by default, neither of which this method
    ///     should ever touch.
    /// </summary>
    private IReadOnlyList<string> ListKeyRingFiles()
    {
        string directory = backupOptions.Value.DataProtectionKeyRingDirectory;
        return Directory.Exists(directory) ? Directory.GetFiles(directory, "key-*.xml") : [];
    }

    /// <summary>
    ///     This method counts a JSON array's own top-level element count without deserializing it
    ///     into a concrete type, purely to populate <see cref="BackupManifest.EntityCounts" />.
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
    ///     This method reads every user-role membership, user claim, and role claim this server
    ///     currently has recorded -- the relationships <see cref="BackupEntityCatalog" /> cannot
    ///     express, since none of the three has its own dedicated <see cref="IRepository{TEntity}" />.
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
    ///     This method removes every currently recorded user-role membership, user claim, and
    ///     role claim, ahead of the users and roles that carry them being wiped and re-imported by
    ///     <see cref="BackupEntityCatalog" /> itself -- neither MongoDB nor this project's own
    ///     use of cascading foreign keys is relied on here, matching the "explicitly clean up a
    ///     dependent row, never assume a cascade will" posture this project already applies to a
    ///     deleted zone's own records and a deleted filter list's own client-group assignments.
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
    ///     This method re-establishes every user-role membership, user claim, and role claim an
    ///     archive recorded, once the users and roles they reference have already been re-imported
    ///     with their own original identifiers intact.
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
    ///     This method deletes every existing Data Protection key ring file, then writes the
    ///     archive's own key files back in its place -- only ever touching files matching that
    ///     format's own naming convention, never the rest of the shared data directory.
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
    ///     This method overwrites the shared TLS certificate's own source file with the archive's
    ///     own copy, if the archive carried one. A certificate an operator explicitly configured
    ///     via <see cref="TlsCertificateOptions.PfxPath" /> is written back to that exact path, so
    ///     a restore never silently switches this server back onto a self-signed certificate.
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
    ///     This method overwrites the runtime configuration file with the archive's own copy, if
    ///     both the archive carried one and this host has one configured to write into.
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
    ///     translating a malformed inner container into <see cref="BackupArchiveInvalidException" />
    ///     rather than letting the raw <see cref="InvalidDataException" /> ZIP itself throws
    ///     escape -- decryption already succeeded by this point, so a failure here means the
    ///     plaintext underneath a correctly decrypted archive simply is not this format.
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
    ///     This method reads one ZIP entry's own text content, throwing
    ///     <see cref="BackupArchiveInvalidException" /> if the entry is missing entirely.
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
    ///     This method reads one ZIP entry's own raw bytes.
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
