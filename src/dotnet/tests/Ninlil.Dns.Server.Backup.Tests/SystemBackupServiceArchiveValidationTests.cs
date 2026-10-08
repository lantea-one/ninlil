using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dhcp.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Backup.Crypto;
using Ninlil.Dns.Server.Backup.Extension;
using Ninlil.Dns.Server.Backup.Options;
using Ninlil.Dns.Server.Core.Certificates;

// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Tests;

/// <summary>
///     This class tests that <see cref="SystemBackupService" /> validates an archive before it
///     changes anything. Each rejection test takes a real backup, edits its ZIP, restores it and
///     checks that <see cref="BackupArchiveInvalidException" /> was thrown and that the database
///     rows, the key ring directory and the configuration file are as they were. It also covers
///     the merge of the key ring, the file modes, the unpacked-size cap and the one-restore-at-a-time
///     gate. File mode assertions return early on Windows.
/// </summary>
[Collection(BackupRestoreCollection.Name)]
public sealed class SystemBackupServiceArchiveValidationTests : IAsyncLifetime
{
    private const string OriginalConfiguration = """{"Original":true}""";
    private const string ValidKeyXml =
        "<key id=\"x\" version=\"1\"><creationDate>now</creationDate></key>";

    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-backup-validation-{Guid.NewGuid()}"
    );

    private string _databasePath;
    private string _keyRingDirectory;
    private string _configurationPath;
    private string _certificatePath;
    private IHost _host;

    public async Task InitializeAsync()
    {
        _keyRingDirectory = Path.Combine(_rootDirectory, "keyring");
        _databasePath = Path.Combine(_rootDirectory, "test.sqlite3");
        _configurationPath = Path.Combine(_rootDirectory, "appsettings.runtime.json");
        _certificatePath = Path.Combine(_rootDirectory, "certificate.pfx");
        Directory.CreateDirectory(_keyRingDirectory);
        await File.WriteAllTextAsync(_configurationPath, OriginalConfiguration);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDhcpEntityFrameworkCore();
        builder
            .Services.AddDataProtection()
            .SetApplicationName("Ninlil.Dns.Server.Backup.Tests")
            .PersistKeysToFileSystem(new DirectoryInfo(_keyRingDirectory));
        builder.Services.AddSingleton(
            new TlsCertificateOptions
            {
                PfxPath = _certificatePath,
                SelfSignedCertificateStatePath = Path.Combine(_rootDirectory, "unused.pfx"),
            }
        );
        builder.Services.AddNinlilDnsServerBackup(builder.Configuration);
        builder.Services.PostConfigure<BackupOptions>(options =>
        {
            options.DataProtectionKeyRingDirectory = _keyRingDirectory;
            options.RuntimeConfigurationFilePath = _configurationPath;
            options.MinimumPassphraseLength = 8;
        });

        _host = builder.Build();

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<NinlilDnsContext>()
            .Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        _host.Dispose();

        if (Directory.Exists(_rootDirectory))
            Directory.Delete(_rootDirectory, recursive: true);

        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("keyring/ninlil.db")]
    [InlineData("keyring/../x")]
    [InlineData("keyring/sub/key-0a1b2c3d-0000-1111-2222-333344445555.xml")]
    [InlineData("keyring/key-not-a-guid.xml")]
    [InlineData("keyring/key-0a1b2c3d-0000-1111-2222-333344445555.xml.bak")]
    public async Task AKeyRingEntryThatIsNotAKeyFileNameIsRejectedAndNothingChanges(string name)
    {
        byte[] archive = EditArchive(
            await CreateArchiveAsync(),
            zip => AddEntry(zip, name, ValidKeyXml)
        );

        Snapshot before = await TakeSnapshotAsync();

        BackupArchiveInvalidException exception =
            await Assert.ThrowsAsync<BackupArchiveInvalidException>(() => RestoreAsync(archive));

        Assert.Contains("unexpected key ring entry", exception.Message);
        await AssertUnchangedAsync(before);
    }

    [Fact]
    public async Task AKeyFileThatIsNotXmlIsRejectedAndNothingChanges()
    {
        byte[] archive = EditArchive(
            await CreateArchiveAsync(),
            zip => AddEntry(zip, $"keyring/key-{Guid.NewGuid()}.xml", "this is not xml")
        );

        Snapshot before = await TakeSnapshotAsync();

        await Assert.ThrowsAsync<BackupArchiveInvalidException>(() => RestoreAsync(archive));

        await AssertUnchangedAsync(before);
    }

    [Fact]
    public async Task AKeyFileWithAnotherRootElementIsRejectedAndNothingChanges()
    {
        byte[] archive = EditArchive(
            await CreateArchiveAsync(),
            zip => AddEntry(zip, $"keyring/key-{Guid.NewGuid()}.xml", "<other />")
        );

        Snapshot before = await TakeSnapshotAsync();

        await Assert.ThrowsAsync<BackupArchiveInvalidException>(() => RestoreAsync(archive));

        await AssertUnchangedAsync(before);
    }

    [Fact]
    public async Task ATwoConfigurationEntriesArchiveIsRejectedAndNothingChanges()
    {
        byte[] archive = EditArchive(
            await CreateArchiveAsync(),
            zip =>
            {
                AddEntry(zip, "config/first.json", "{}");
                AddEntry(zip, "config/second.json", "{}");
            }
        );

        Snapshot before = await TakeSnapshotAsync();

        BackupArchiveInvalidException exception =
            await Assert.ThrowsAsync<BackupArchiveInvalidException>(() => RestoreAsync(archive));

        Assert.Contains("more than one 'config/' entry", exception.Message);
        await AssertUnchangedAsync(before);
    }

    [Fact]
    public async Task ATwoCertificateEntriesArchiveIsRejectedAndNothingChanges()
    {
        byte[] archive = EditArchive(
            await CreateArchiveAsync(),
            zip =>
            {
                AddEntry(zip, "certificate/first.pfx", "pfx-one");
                AddEntry(zip, "certificate/second.pfx", "pfx-two");
            }
        );

        Snapshot before = await TakeSnapshotAsync();

        BackupArchiveInvalidException exception =
            await Assert.ThrowsAsync<BackupArchiveInvalidException>(() => RestoreAsync(archive));

        Assert.Contains("more than one 'certificate/' entry", exception.Message);
        await AssertUnchangedAsync(before);
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("[1, 2]")]
    [InlineData("null")]
    public async Task AConfigurationThatIsNotAJsonObjectIsRejectedAndNothingChanges(string content)
    {
        byte[] archive = EditArchive(
            await CreateArchiveAsync(),
            zip => AddEntry(zip, "config/appsettings.runtime.json", content)
        );

        Snapshot before = await TakeSnapshotAsync();

        BackupArchiveInvalidException exception =
            await Assert.ThrowsAsync<BackupArchiveInvalidException>(() => RestoreAsync(archive));

        Assert.Contains("not a JSON object", exception.Message);
        await AssertUnchangedAsync(before);
    }

    [Fact]
    public async Task APayloadPastTheUnpackedSizeCapIsRejectedAndNothingChanges()
    {
        byte[] original = await CreateArchiveAsync();
        long unpackedSize = UnpackedSize(original);

        // A highly compressible entry: tiny in the ZIP, large once unpacked, like a zip bomb.
        byte[] bomb = EditArchive(
            original,
            zip =>
                AddEntry(
                    zip,
                    "config/appsettings.runtime.json",
                    "{\"Padding\":\"" + new string('a', 50_000) + "\"}"
                )
        );

        SystemBackupService cappedService = CreateService(
            maximumRestoredPayloadBytes: unpackedSize + 1_000
        );
        Snapshot before = await TakeSnapshotAsync();

        BackupArchiveInvalidException exception =
            await Assert.ThrowsAsync<BackupArchiveInvalidException>(() =>
                cappedService.RestoreBackupAsync(new MemoryStream(bomb), null)
            );

        Assert.Contains("more data than a restore accepts", exception.Message);
        await AssertUnchangedAsync(before);

        // The same cap accepts the untouched archive, so it was the extra entry that tripped it.
        await cappedService.RestoreBackupAsync(new MemoryStream(original), null);
    }

    [Fact]
    public async Task AValidRestoreMergesTheKeyRingKeepsItOwnerOnlyAndLeavesNoTemporaryFiles()
    {
        byte[] archive = await CreateArchiveAsync();
        string[] archiveKeyFiles = Directory
            .GetFiles(_keyRingDirectory, "key-*.xml")
            .Select(Path.GetFileName)
            .ToArray();
        Assert.NotEmpty(archiveKeyFiles);

        // A key created after the backup, and a key file the archive also carries but with other
        // content: the first must survive (merge), the second is replaced by the archive's copy.
        string extraKey = Path.Combine(_keyRingDirectory, $"key-{Guid.NewGuid()}.xml");
        await File.WriteAllTextAsync(extraKey, ValidKeyXml);
        string sharedKey = Path.Combine(_keyRingDirectory, archiveKeyFiles[0]);
        string archiveCopy = await File.ReadAllTextAsync(sharedKey);
        await File.WriteAllTextAsync(sharedKey, "<key id=\"stale\" />");

        await RestoreAsync(archive);

        Assert.True(File.Exists(extraKey));
        Assert.Equal(archiveCopy, await File.ReadAllTextAsync(sharedKey));
        Assert.Empty(Directory.GetFiles(_keyRingDirectory, "*.tmp"));
        Assert.Empty(Directory.GetFiles(_rootDirectory, "*.tmp"));

        if (OperatingSystem.IsWindows())
            return;

        foreach (string keyFile in Directory.GetFiles(_keyRingDirectory, "key-*.xml"))
            if (keyFile != extraKey)
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    File.GetUnixFileMode(keyFile)
                );
    }

    [Fact]
    public async Task APlainKeyRingDirectoryEntryDoesNotBreakTheRestore()
    {
        byte[] archive = EditArchive(
            await CreateArchiveAsync(),
            zip =>
            {
                zip.CreateEntry("keyring/");
                zip.CreateEntry("certificate/");
                zip.CreateEntry("config/");
            }
        );

        await RestoreAsync(archive);

        Assert.Equal(OriginalConfiguration, await File.ReadAllTextAsync(_configurationPath));
    }

    [Fact]
    public async Task ARestoreKeepsTheExistingConfigurationFileMode()
    {
        if (OperatingSystem.IsWindows())
            return;

        byte[] archive = await CreateArchiveAsync();
        UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
        File.SetUnixFileMode(_configurationPath, mode);

        await RestoreAsync(archive);

        Assert.Equal(mode, File.GetUnixFileMode(_configurationPath));
    }

    [Fact]
    public async Task ASecondConcurrentRestoreIsRefusedAndTheFirstStillCompletes()
    {
        byte[] archive = await CreateArchiveAsync();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        BlockingStream blockingStream = new(archive, started, release.Task);

        IBackupService service = _host.Services.GetRequiredService<IBackupService>();
        Task<Ninlil.Dns.Server.Backup.Model.BackupRestoreSummary> first =
            service.RestoreBackupAsync(blockingStream, null);
        await started.Task;

        await Assert.ThrowsAsync<BackupRestoreInProgressException>(() => RestoreAsync(archive));

        release.SetResult();
        await first;

        // The gate is free again.
        await RestoreAsync(archive);
    }

    /// <summary>
    ///     This method creates an unencrypted backup of the current test state: one zone row, key
    ///     ring files, the original configuration file and a certificate file.
    /// </summary>
    private async Task<byte[]> CreateArchiveAsync()
    {
        // Protecting a value makes Data Protection create its key file in the key ring directory.
        DataProtectionProvider
            .Create(new DirectoryInfo(_keyRingDirectory), builder => { })
            .CreateProtector("Ninlil.Dns.Server.Backup.Tests.Validation")
            .Protect("seed");
        await File.WriteAllBytesAsync(_certificatePath, [1, 2, 3]);

        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IZoneRepository>()
                .AddAsync(
                    new ZoneEntity
                    {
                        Id = Guid.NewGuid(),
                        Name = "validation.example",
                        Type = ZoneTypeEnum.Primary,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    }
                );
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        IBackupService service = _host.Services.GetRequiredService<IBackupService>();
        return await service.CreateBackupAsync(null);
    }

    private async Task RestoreAsync(byte[] archive)
    {
        IBackupService service = _host.Services.GetRequiredService<IBackupService>();
        await service.RestoreBackupAsync(new MemoryStream(archive), null);
    }

    private SystemBackupService CreateService(long maximumRestoredPayloadBytes)
    {
        BackupOptions options = new()
        {
            DataProtectionKeyRingDirectory = _keyRingDirectory,
            RuntimeConfigurationFilePath = _configurationPath,
            MaximumRestoredPayloadBytes = maximumRestoredPayloadBytes,
        };

        return new SystemBackupService(
            _host.Services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(options),
            _host.Services.GetRequiredService<TlsCertificateOptions>(),
            _host.Services.GetRequiredService<StorageOptions>(),
            NullLogger<SystemBackupService>.Instance
        );
    }

    /// <summary>
    ///     This method unwraps an unencrypted archive, lets <paramref name="edit" /> change its
    ///     ZIP and wraps it again.
    /// </summary>
    private static byte[] EditArchive(byte[] archive, Action<ZipArchive> edit)
    {
        using MemoryStream zipStream = new();
        byte[] plaintext = BackupArchiveCodec.Unwrap(archive);
        zipStream.Write(plaintext);

        using (ZipArchive zip = new(zipStream, ZipArchiveMode.Update, leaveOpen: true))
            edit(zip);

        return BackupArchiveCodec.Wrap(zipStream.ToArray());
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        // An entry with the same name is replaced, as "config/..." is already in the archive.
        zip.GetEntry(name)?.Delete();
        ZipArchiveEntry entry = zip.CreateEntry(name);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static long UnpackedSize(byte[] archive)
    {
        using MemoryStream stream = new(BackupArchiveCodec.Unwrap(archive));
        using ZipArchive zip = new(stream, ZipArchiveMode.Read);
        return zip.Entries.Sum(entry => entry.Length);
    }

    private sealed record Snapshot(
        int ZoneCount,
        string[] KeyRingFiles,
        string[] KeyRingContents,
        string Configuration,
        byte[] Certificate
    );

    private async Task<Snapshot> TakeSnapshotAsync()
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        int zones = (
            await scope.ServiceProvider.GetRequiredService<IZoneRepository>().ListAsync()
        ).Count;

        string[] files =
        [
            .. Directory
                .GetFiles(_rootDirectory, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".sqlite3") && !path.Contains(".sqlite3-"))
                .Order(StringComparer.Ordinal),
        ];

        return new Snapshot(
            zones,
            files,
            [.. files.Select(File.ReadAllText)],
            await File.ReadAllTextAsync(_configurationPath),
            await File.ReadAllBytesAsync(_certificatePath)
        );
    }

    private async Task AssertUnchangedAsync(Snapshot before)
    {
        Snapshot after = await TakeSnapshotAsync();

        Assert.Equal(before.ZoneCount, after.ZoneCount);
        Assert.Equal(before.KeyRingFiles, after.KeyRingFiles);
        Assert.Equal(before.KeyRingContents, after.KeyRingContents);
        Assert.Equal(before.Configuration, after.Configuration);
        Assert.Equal(before.Certificate, after.Certificate);
    }

    /// <summary>
    ///     This stream serves an archive but holds the first read until the test lets it go, so
    ///     the restore that is reading it keeps the restore gate meanwhile.
    /// </summary>
    private sealed class BlockingStream(byte[] content, TaskCompletionSource started, Task release)
        : MemoryStream(content)
    {
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            started.TrySetResult();
            await release;
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}
