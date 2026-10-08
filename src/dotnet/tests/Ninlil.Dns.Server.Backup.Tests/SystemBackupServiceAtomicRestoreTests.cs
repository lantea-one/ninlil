using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
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
///     This class tests that <see cref="SystemBackupService" /> restores all or nothing on SQLite:
///     malformed entity JSON is rejected before the wipe, a database failure in the middle of the
///     import rolls the wipe back and leaves the staged files uncommitted, and an archive from
///     before the cluster key link gets its peer keys linked.
/// </summary>
[Collection(BackupRestoreCollection.Name)]
public sealed class SystemBackupServiceAtomicRestoreTests : IAsyncLifetime
{
    private const string OriginalConfiguration = """{"Original":true}""";

    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-backup-atomic-{Guid.NewGuid()}"
    );

    private string _keyRingDirectory;
    private string _configurationPath;
    private string _certificatePath;
    private IHost _host;

    public async Task InitializeAsync()
    {
        _keyRingDirectory = Path.Combine(_rootDirectory, "keyring");
        _configurationPath = Path.Combine(_rootDirectory, "appsettings.runtime.json");
        _certificatePath = Path.Combine(_rootDirectory, "certificate.pfx");
        Directory.CreateDirectory(_keyRingDirectory);
        await File.WriteAllTextAsync(_configurationPath, OriginalConfiguration);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] =
            $"Data Source={Path.Combine(_rootDirectory, "test.sqlite3")}";
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

    [Fact]
    public async Task MalformedDhcpLeasesJsonIsRejectedBeforeTheWipeAndEveryRowSurvives()
    {
        byte[] archive = EditArchive(
            await CreateArchiveAsync(),
            zip => ReplaceEntry(zip, "database/DhcpLeases.json", "{ this is not json")
        );
        string[] filesBefore = ListFiles();

        BackupArchiveInvalidException exception =
            await Assert.ThrowsAsync<BackupArchiveInvalidException>(() => RestoreAsync(archive));

        Assert.Contains("database/DhcpLeases.json", exception.Message);
        Assert.Equal(["validation.example"], await ZoneNamesAsync());
        Assert.Equal(filesBefore, ListFiles());
    }

    [Fact]
    public async Task ADatabaseFailureAfterTheWipeRollsBackAndLeavesTheFilesAlone()
    {
        byte[] original = await CreateArchiveAsync();

        // The archive is valid JSON, so it passes the parse step, but two leases share an Id and
        // the database insert fails after the zones, records and everything else were wiped.
        Guid sharedId = Guid.NewGuid();
        string duplicateLeases = Ninlil.Common.Serializer.JsonSerializer.Serialize(
            new List<LeaseEntity> { NewLease(sharedId, "10.0.0.1"), NewLease(sharedId, "10.0.0.2") }
        );
        byte[] archive = EditArchive(
            original,
            zip =>
            {
                ReplaceEntry(zip, "database/DhcpLeases.json", duplicateLeases);
                ReplaceEntry(zip, "database/Zones.json", "[]");
                ReplaceEntry(zip, "database/Records.json", "[]");
                ReplaceEntry(zip, "config/appsettings.runtime.json", """{"FromArchive":true}""");
                ReplaceEntry(zip, $"keyring/key-{Guid.NewGuid()}.xml", "<key id=\"new\" />");
            }
        );
        string[] filesBefore = ListFiles();
        string[] contentsBefore = [.. filesBefore.Select(File.ReadAllText)];

        await Assert.ThrowsAnyAsync<Exception>(() => RestoreAsync(archive));

        // The wiped zone is back, nothing from the archive reached the disk, and no staged
        // temporary file is left behind.
        Assert.Equal(["validation.example"], await ZoneNamesAsync());
        Assert.Equal(filesBefore, ListFiles());
        Assert.Equal(contentsBefore, ListFiles().Select(File.ReadAllText));
        Assert.Equal(OriginalConfiguration, await File.ReadAllTextAsync(_configurationPath));
        Assert.Empty(Directory.GetFiles(_rootDirectory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task AnArchiveFromBeforeTheClusterKeyLinkRestoresWithThePeerKeyLinked()
    {
        Guid nodeId = Guid.NewGuid();
        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            IServiceProvider services = scope.ServiceProvider;
            await services
                .GetRequiredService<IClusterNodeRepository>()
                .AddAsync(
                    new ClusterNodeEntity
                    {
                        Id = nodeId,
                        Name = "peer",
                        Host = "peer.example",
                        Port = 5380,
                        RemoteNodeId = Guid.NewGuid(),
                        TsigKeyName = "peer-key",
                        TlsCertificateThumbprint = "ab",
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    }
                );
            foreach (string name in new[] { "peer-key", "invite-key" })
                await services
                    .GetRequiredService<ITsigKeyRepository>()
                    .AddAsync(
                        new TsigKeyEntity
                        {
                            Id = Guid.NewGuid(),
                            Name = name,
                            AlgorithmName = "hmac-sha256",
                            SecretEncrypted = "x",
                            CreatedAtUtc = DateTimeOffset.UtcNow,
                        }
                    );
            await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        // Remove the property from every element, as an archive from before the upgrade lacks it.
        byte[] archive = EditArchive(
            await CreateArchiveAsync(),
            zip =>
            {
                ZipArchiveEntry entry = zip.GetEntry("database/TsigKeys.json");
                JsonArray keys;
                using (StreamReader reader = new(entry.Open()))
                    keys = JsonNode.Parse(reader.ReadToEnd()).AsArray();

                foreach (JsonNode key in keys)
                {
                    JsonObject keyObject = key.AsObject();
                    string propertyName = keyObject
                        .Select(pair => pair.Key)
                        .Single(name =>
                            string.Equals(name, "ClusterNodeId", StringComparison.OrdinalIgnoreCase)
                        );
                    keyObject.Remove(propertyName);
                }

                ReplaceEntry(zip, "database/TsigKeys.json", keys.ToJsonString());
            }
        );

        await RestoreAsync(archive);

        await using AsyncServiceScope restored = _host.Services.CreateAsyncScope();
        ITsigKeyRepository repository =
            restored.ServiceProvider.GetRequiredService<ITsigKeyRepository>();
        Assert.Equal(nodeId, (await repository.FindByNameAsync("peer-key")).ClusterNodeId);
        Assert.Null((await repository.FindByNameAsync("invite-key")).ClusterNodeId);
    }

    private static LeaseEntity NewLease(Guid id, string address) =>
        new()
        {
            Id = id,
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            ScopeName = "scope",
            ClientIdentifier = "client",
            IpAddress = address,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

    private async Task<byte[]> CreateArchiveAsync()
    {
        // Protecting a value makes Data Protection create its key file in the key ring directory.
        DataProtectionProvider
            .Create(new DirectoryInfo(_keyRingDirectory), builder => { })
            .CreateProtector("Ninlil.Dns.Server.Backup.Tests.Atomic")
            .Protect("seed");
        await File.WriteAllBytesAsync(_certificatePath, [1, 2, 3]);

        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            if (
                (
                    await scope.ServiceProvider.GetRequiredService<IZoneRepository>().ListAsync()
                ).Count == 0
            )
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
        }

        return await _host.Services.GetRequiredService<IBackupService>().CreateBackupAsync(null);
    }

    private Task<Model.BackupRestoreSummary> RestoreAsync(byte[] archive) =>
        _host
            .Services.GetRequiredService<IBackupService>()
            .RestoreBackupAsync(new MemoryStream(archive), null);

    private async Task<string[]> ZoneNamesAsync()
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        return
        [
            .. (
                await scope.ServiceProvider.GetRequiredService<IZoneRepository>().ListAsync()
            ).Select(zone => zone.Name),
        ];
    }

    private string[] ListFiles() =>
        [
            .. Directory
                .GetFiles(_rootDirectory, "*", SearchOption.AllDirectories)
                .Where(path => !path.Contains(".sqlite3"))
                .Order(StringComparer.Ordinal),
        ];

    private static byte[] EditArchive(byte[] archive, Action<ZipArchive> edit)
    {
        using MemoryStream zipStream = new();
        zipStream.Write(BackupArchiveCodec.Unwrap(archive));

        using (ZipArchive zip = new(zipStream, ZipArchiveMode.Update, leaveOpen: true))
            edit(zip);

        return BackupArchiveCodec.Wrap(zipStream.ToArray());
    }

    private static void ReplaceEntry(ZipArchive zip, string name, string content)
    {
        zip.GetEntry(name)?.Delete();
        ZipArchiveEntry entry = zip.CreateEntry(name);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
