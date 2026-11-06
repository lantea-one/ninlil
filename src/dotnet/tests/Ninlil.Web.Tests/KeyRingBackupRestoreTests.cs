using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dhcp.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Backup;
using Ninlil.Dns.Server.Backup.Extension;
using Ninlil.Dns.Server.Backup.Options;
using Ninlil.Dns.Server.Core.Certificates;
using Ninlil.Web.DataProtection;
using Ninlil.Web.Options;
using Ninlil.Web.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class tests a backup and restore of a certificate-protected key ring with the real
///     <see cref="SystemBackupService" /> and <see cref="KeyRingRestoreCheck" />: a restore on
///     the same host keeps every secret readable, and a restore where the certificate is missing
///     or different is refused with a message that names the setting before anything changes.
///     It uses a small host of its own (SQLite in a temporary file, a key ring in a temporary
///     directory, certificates generated in process), not the shared web host, because a restore
///     replaces the database and the key ring.
/// </summary>
[Collection(WebTestCollection.Name)]
public sealed class KeyRingBackupRestoreTests : IDisposable
{
    private readonly string _root = KeyRingTestCertificates.NewDirectory();

    /// <summary>
    ///     This method deletes the temporary directory.
    /// </summary>
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    /// <summary>
    ///     A backup restored on the host that took it passes the check, and the secret protected
    ///     before the backup still decrypts afterwards.
    /// </summary>
    [Fact]
    public async Task Restore_OnTheSameHost_KeepsASecretProtectedBeforeTheBackupReadable()
    {
        string pfx = Path.Combine(_root, "current.pfx");
        KeyRingTestCertificates.WriteRsaPfx(pfx, "pw");
        DataProtectionKeyRingOptions options = new()
        {
            CertificatePath = pfx,
            CertificatePassword = "pw",
        };

        using IHost host = await BuildHostAsync("one", options);
        byte[] secret = Protect(host, "stored-secret");

        byte[] archive = await host
            .Services.GetRequiredService<IServiceScopeFactory>()
            .CreateScope()
            .ServiceProvider.GetRequiredService<IBackupService>()
            .CreateBackupAsync(null);

        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IBackupService>()
            .RestoreBackupAsync(new MemoryStream(archive), null);

        Assert.Equal("stored-secret", Unprotect(host, secret));
    }

    /// <summary>
    ///     A backup restored on a host that has a different certificate is refused with a message
    ///     that names the previous-certificate setting, and the key ring directory is unchanged.
    /// </summary>
    [Fact]
    public async Task Restore_OnAHostWithAnotherCertificate_IsRefusedNamingTheSetting()
    {
        string pfxOne = Path.Combine(_root, "one.pfx");
        string pfxTwo = Path.Combine(_root, "two.pfx");
        KeyRingTestCertificates.WriteRsaPfx(pfxOne, null);
        KeyRingTestCertificates.WriteRsaPfx(pfxTwo, null);

        using IHost source = await BuildHostAsync(
            "source",
            new DataProtectionKeyRingOptions { CertificatePath = pfxOne }
        );
        Protect(source, "x");
        byte[] archive = await source
            .Services.CreateAsyncScope()
            .ServiceProvider.GetRequiredService<IBackupService>()
            .CreateBackupAsync(null);

        using IHost target = await BuildHostAsync(
            "target",
            new DataProtectionKeyRingOptions { CertificatePath = pfxTwo }
        );
        string targetRing = Path.Combine(_root, "target", "ring");
        string[] before = Directory.GetFiles(targetRing);

        await using AsyncServiceScope scope = target.Services.CreateAsyncScope();
        BackupArchiveInvalidException exception =
            await Assert.ThrowsAsync<BackupArchiveInvalidException>(() =>
                scope
                    .ServiceProvider.GetRequiredService<IBackupService>()
                    .RestoreBackupAsync(new MemoryStream(archive), null)
            );

        Assert.Contains("DataProtection:PreviousCertificatePath", exception.Message);
        Assert.Equal(before.Order(), Directory.GetFiles(targetRing).Order());
    }

    /// <summary>
    ///     Once the source certificate is set as the target's previous certificate the same
    ///     archive restores, and the source's secret decrypts on the target.
    /// </summary>
    [Fact]
    public async Task Restore_WithTheSourceCertificateAsPrevious_SucceedsAndTheSecretDecrypts()
    {
        string pfxOne = Path.Combine(_root, "one.pfx");
        string pfxTwo = Path.Combine(_root, "two.pfx");
        KeyRingTestCertificates.WriteRsaPfx(pfxOne, null);
        KeyRingTestCertificates.WriteRsaPfx(pfxTwo, null);

        using IHost source = await BuildHostAsync(
            "source",
            new DataProtectionKeyRingOptions { CertificatePath = pfxOne }
        );
        byte[] secret = Protect(source, "moved-secret");
        byte[] archive = await source
            .Services.CreateAsyncScope()
            .ServiceProvider.GetRequiredService<IBackupService>()
            .CreateBackupAsync(null);

        using IHost target = await BuildHostAsync(
            "target",
            new DataProtectionKeyRingOptions
            {
                CertificatePath = pfxTwo,
                PreviousCertificatePath = pfxOne,
            }
        );
        await using AsyncServiceScope scope = target.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IBackupService>()
            .RestoreBackupAsync(new MemoryStream(archive), null);

        Assert.Equal("moved-secret", Unprotect(target, secret));
    }

    private static byte[] Protect(IHost host, string text) =>
        host
            .Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("test")
            .Protect(System.Text.Encoding.UTF8.GetBytes(text));

    private static string Unprotect(IHost host, byte[] data) =>
        System.Text.Encoding.UTF8.GetString(
            host.Services.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("test")
                .Unprotect(data)
        );

    /// <summary>
    ///     This method builds a host the way <c>Program.cs</c> wires the key ring and the backup
    ///     service: protection from the options, the restore check registered, and the backup
    ///     service pointed at the host's own key ring directory.
    /// </summary>
    /// <param name="name">The host's folder name under the test root.</param>
    /// <param name="options">The key ring settings.</param>
    private async Task<IHost> BuildHostAsync(string name, DataProtectionKeyRingOptions options)
    {
        string directory = Path.Combine(_root, name);
        string ring = Path.Combine(directory, "ring");
        Directory.CreateDirectory(ring);
        string configuration = Path.Combine(directory, "appsettings.runtime.json");
        await File.WriteAllTextAsync(configuration, "{}");

        KeyRingProtection protection = KeyRingProtection.Resolve(
            options,
            OperatingSystem.IsWindows()
        );
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] =
            $"Data Source={Path.Combine(directory, "test.sqlite3")}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDhcpEntityFrameworkCore();
        builder.Services.AddSingleton(_ => protection);
        builder.Services.AddSingleton<IKeyRingRestoreCheck, KeyRingRestoreCheck>();
        protection.Apply(
            builder
                .Services.AddDataProtection()
                .SetApplicationName("Ninlil.Dns.KeyRingBackupTests")
                .PersistKeysToFileSystem(new DirectoryInfo(ring))
        );
        builder.Services.AddSingleton(
            new TlsCertificateOptions
            {
                SelfSignedCertificateStatePath = Path.Combine(directory, "unused.pfx"),
            }
        );
        // Registered before the backup services, whose own default registration only applies
        // when the host has none.
        builder.Services.AddSingleton(
            new BackupPathSettings
            {
                DataProtectionKeyRingDirectory = ring,
                RuntimeConfigurationFilePath = configuration,
            }
        );
        builder.Services.AddNinlilDnsServerBackup(builder.Configuration);
        builder.Services.PostConfigure<BackupOptions>(backup => backup.MinimumPassphraseLength = 8);

        IHost host = builder.Build();
        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
            await scope
                .ServiceProvider.GetRequiredService<NinlilDnsContext>()
                .Database.EnsureCreatedAsync();
        return host;
    }
}
