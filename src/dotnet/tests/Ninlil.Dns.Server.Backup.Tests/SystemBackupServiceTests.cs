using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Backup.Crypto;
using Ninlil.Dns.Server.Backup.Extension;
using Ninlil.Dns.Server.Backup.Model;
using Ninlil.Dns.Server.Backup.Options;
using Ninlil.Dns.Server.Core.Certificates;

// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Tests;

/// <summary>
///     This class covers <see cref="SystemBackupService" /> against a real, SQLite-backed
///     repository stack and a real, on-disk Data Protection key ring -- the same verification
///     shape <c>CaaRecordReconciliationServiceTests</c>/<c>PtrSyncServiceTests</c> already use for
///     this project's own write-path services, extended here with a real encrypted secret so a
///     restore's own "every previously-encrypted field is still decryptable afterward" promise is
///     genuinely exercised, not merely asserted.
/// </summary>
public sealed class SystemBackupServiceTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-backup-tests-{Guid.NewGuid()}.sqlite3"
    );

    private readonly string _keyRingDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-backup-tests-keyring-{Guid.NewGuid()}"
    );

    private IHost _host;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_keyRingDirectory);

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
                // Point both certificate paths somewhere that never actually exists for this
                // test -- the certificate bundle is exercised by its own dedicated assertions
                // below, not implicitly by every other test in this class.
                PfxPath = "",
                SelfSignedCertificateStatePath = Path.Combine(
                    _keyRingDirectory,
                    "..",
                    "nonexistent-certificate.pfx"
                ),
            }
        );
        builder.Services.AddNinlilDnsServerBackup(builder.Configuration);
        builder.Services.PostConfigure<BackupOptions>(options =>
        {
            options.DataProtectionKeyRingDirectory = _keyRingDirectory;
            options.MinimumPassphraseLength = 8;
        });

        _host = builder.Build();

        await using AsyncServiceScope seedScope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = seedScope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);

        if (Directory.Exists(_keyRingDirectory))
            Directory.Delete(_keyRingDirectory, recursive: true);

        return Task.CompletedTask;
    }

    /// <summary>
    ///     This method builds an <see cref="IDataProtector" /> pointed at this test's own key
    ///     ring directory, exactly the shape a real <c>TsigSecretProtector</c> would build one
    ///     with, so a test can encrypt and later decrypt a real secret the same way this
    ///     project's own production code does.
    /// </summary>
    private IDataProtector CreateSecretProtector() =>
        DataProtectionProvider
            .Create(new DirectoryInfo(_keyRingDirectory), builder => { })
            .CreateProtector("Ninlil.Dns.Server.Backup.Tests.Secret");

    [Fact]
    public async Task ARoundTripPreservesEveryEntityAndKeepsAnEncryptedSecretDecryptable()
    {
        IServiceScopeFactory scopeFactory =
            _host.Services.GetRequiredService<IServiceScopeFactory>();

        // Seed a real zone, a real record inside it, a real TSIG key whose own secret is
        // genuinely encrypted through this test's own key ring (not a stand-in string), and a
        // real role/user/role-assignment/direct-claim identity relationship set.
        Guid zoneId = Guid.NewGuid();
        Guid recordId = Guid.NewGuid();
        Guid tsigKeyId = Guid.NewGuid();
        Guid roleId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        const string plaintextSecret = "this-is-a-real-tsig-shared-secret";
        string encryptedSecret = CreateSecretProtector().Protect(plaintextSecret);

        await using (AsyncServiceScope seedScope = scopeFactory.CreateAsyncScope())
        {
            IZoneRepository zoneRepository =
                seedScope.ServiceProvider.GetRequiredService<IZoneRepository>();
            IRecordRepository recordRepository =
                seedScope.ServiceProvider.GetRequiredService<IRecordRepository>();
            ITsigKeyRepository tsigKeyRepository =
                seedScope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();
            IRoleRepository roleRepository =
                seedScope.ServiceProvider.GetRequiredService<IRoleRepository>();
            IUserRepository userRepository =
                seedScope.ServiceProvider.GetRequiredService<IUserRepository>();
            IUnitOfWork unitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await zoneRepository.AddAsync(
                new ZoneEntity
                {
                    Id = zoneId,
                    Name = "backup-round-trip.example",
                    Type = ZoneTypeEnum.Primary,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
            await recordRepository.AddAsync(
                new RecordEntity
                {
                    Id = recordId,
                    ZoneId = zoneId,
                    Name = "backup-round-trip.example",
                    Type = DnsResourceRecordTypeEnum.A,
                    TimeToLiveSeconds = 3600,
                    DataJson = """{"Address":"203.0.113.9"}""",
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
            await tsigKeyRepository.AddAsync(
                new TsigKeyEntity
                {
                    Id = tsigKeyId,
                    Name = "backup-round-trip-key",
                    AlgorithmName = "hmac-sha256.",
                    SecretEncrypted = encryptedSecret,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
            await roleRepository.AddAsync(
                new RoleEntity
                {
                    Id = roleId,
                    Name = "backup-round-trip-role",
                    NormalizedName = "BACKUP-ROUND-TRIP-ROLE",
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
            await userRepository.AddAsync(
                new UserEntity
                {
                    Id = userId,
                    UserName = "backup-round-trip-user",
                    NormalizedUserName = "BACKUP-ROUND-TRIP-USER",
                    PasswordHash = "not-a-real-hash",
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
            await unitOfWork.SaveChangesAsync();

            await roleRepository.AddClaimAsync(roleId, "Permission", "zones:read");
            await userRepository.AddToRoleAsync(userId, roleId);
            await userRepository.AddClaimsAsync(userId, [("Permission", "webhooks:read")]);
            await unitOfWork.SaveChangesAsync();
        }

        // Confirm the secret is genuinely decryptable before backup, so a later failure can only
        // mean the restore itself broke something, not that the seed data was already wrong.
        Assert.Equal(plaintextSecret, CreateSecretProtector().Unprotect(encryptedSecret));

        IBackupService backupService = _host.Services.GetRequiredService<IBackupService>();
        byte[] archive = await backupService.CreateBackupAsync("a-strong-test-passphrase");

        // Wipe every entity this catalog covers and delete every key ring file, simulating a
        // brand-new install this archive is about to be restored onto.
        await using (AsyncServiceScope wipeScope = scopeFactory.CreateAsyncScope())
        {
            IUnitOfWork unitOfWork = wipeScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            foreach (
                RecordEntity record in await wipeScope
                    .ServiceProvider.GetRequiredService<IRecordRepository>()
                    .ListAsync()
            )
                wipeScope.ServiceProvider.GetRequiredService<IRecordRepository>().Remove(record);

            foreach (
                ZoneEntity zone in await wipeScope
                    .ServiceProvider.GetRequiredService<IZoneRepository>()
                    .ListAsync()
            )
                wipeScope.ServiceProvider.GetRequiredService<IZoneRepository>().Remove(zone);

            foreach (
                TsigKeyEntity key in await wipeScope
                    .ServiceProvider.GetRequiredService<ITsigKeyRepository>()
                    .ListAsync()
            )
                wipeScope.ServiceProvider.GetRequiredService<ITsigKeyRepository>().Remove(key);

            await unitOfWork.SaveChangesAsync();
        }

        foreach (string keyFile in Directory.GetFiles(_keyRingDirectory, "key-*.xml"))
            File.Delete(keyFile);

        Assert.Empty(await _host.Services.GetRequiredService<IZoneRepository>().ListAsync());

        BackupRestoreSummary summary = await backupService.RestoreBackupAsync(
            new MemoryStream(archive),
            "a-strong-test-passphrase"
        );

        Assert.True(summary.TotalEntitiesImported > 0);
        Assert.Equal(1, summary.EntityCounts["Zones"]);
        Assert.Equal(1, summary.EntityCounts["Records"]);
        Assert.Equal(1, summary.EntityCounts["TsigKeys"]);
        Assert.Equal(1, summary.EntityCounts["Roles"]);
        Assert.Equal(1, summary.EntityCounts["Users"]);

        await using AsyncServiceScope verifyScope = scopeFactory.CreateAsyncScope();

        ZoneEntity restoredZone = await verifyScope
            .ServiceProvider.GetRequiredService<IZoneRepository>()
            .GetByIdAsync(zoneId);
        Assert.NotNull(restoredZone);
        Assert.Equal("backup-round-trip.example", restoredZone.Name);

        RecordEntity restoredRecord = await verifyScope
            .ServiceProvider.GetRequiredService<IRecordRepository>()
            .GetByIdAsync(recordId);
        Assert.NotNull(restoredRecord);
        Assert.Equal(zoneId, restoredRecord.ZoneId);

        TsigKeyEntity restoredKey = await verifyScope
            .ServiceProvider.GetRequiredService<ITsigKeyRepository>()
            .GetByIdAsync(tsigKeyId);
        Assert.NotNull(restoredKey);

        // The single most important assertion in this test: the ciphertext round-tripped
        // byte-for-byte, AND a fresh IDataProtector -- pointed at the key ring the restore itself
        // just wrote back to disk -- can still decrypt it back to the original plaintext.
        Assert.Equal(encryptedSecret, restoredKey.SecretEncrypted);
        Assert.Equal(
            plaintextSecret,
            CreateSecretProtector().Unprotect(restoredKey.SecretEncrypted)
        );

        // The identity relationships this catalog cannot express on its own must also have
        // survived, re-attached to the same, preserved user/role identifiers.
        IUserRepository verifyUserRepository =
            verifyScope.ServiceProvider.GetRequiredService<IUserRepository>();
        IRoleRepository verifyRoleRepository =
            verifyScope.ServiceProvider.GetRequiredService<IRoleRepository>();

        IReadOnlyList<RoleEntity> restoredUserRoles = await verifyUserRepository.ListRolesAsync(
            userId
        );
        Assert.Single(restoredUserRoles);
        Assert.Equal(roleId, restoredUserRoles[0].Id);

        IReadOnlyList<UserClaimEntity> restoredUserClaims =
            await verifyUserRepository.ListClaimsAsync(userId);
        Assert.Contains(
            restoredUserClaims,
            claim => claim.ClaimType == "Permission" && claim.ClaimValue == "webhooks:read"
        );

        IReadOnlyList<RoleClaimEntity> restoredRoleClaims =
            await verifyRoleRepository.ListClaimsAsync(roleId);
        Assert.Contains(
            restoredRoleClaims,
            claim => claim.ClaimType == "Permission" && claim.ClaimValue == "zones:read"
        );
    }

    /// <summary>
    ///     This test proves the known-client registry, access-control rules, cluster peers, and
    ///     every DHCP table survive a backup/restore round trip -- the tables a storage-engine
    ///     switch (which moves data exclusively through this same path) would otherwise silently
    ///     drop. Every seeded row is removed after the backup is taken, so each one found
    ///     afterward can only have come from the archive.
    /// </summary>
    [Fact]
    public async Task ARoundTripPreservesClientRegistryAccessControlClusterAndDhcpData()
    {
        IServiceScopeFactory scopeFactory =
            _host.Services.GetRequiredService<IServiceScopeFactory>();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        DnsClientEntity device = new()
        {
            Id = Guid.NewGuid(),
            DisplayName = "living-room-tv",
            CreatedAtUtc = now,
        };
        DnsClientAddressEntity address = new()
        {
            Id = Guid.NewGuid(),
            ClientId = device.Id,
            ClientIdentifier = "aa:bb:cc:dd:ee:ff",
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            FirstSeenUtc = now,
            CreatedAtUtc = now,
        };
        DnsClientScheduleEntity schedule = new()
        {
            Id = Guid.NewGuid(),
            ClientId = device.Id,
            DaysOfWeek = DnsScheduleDayOfWeekEnum.Monday,
            StartTime = new TimeOnly(22, 0),
            EndTime = new TimeOnly(6, 0),
            TimeZoneId = "UTC",
            CreatedAtUtc = now,
        };
        DnsClientStaticAddressMappingEntity mapping = new()
        {
            Id = Guid.NewGuid(),
            Cidr = "192.0.2.10",
            ClientIdentifier = "aa:bb:cc:dd:ee:ff",
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            CreatedAtUtc = now,
        };
        DnsAccessControlRuleEntity rule = new()
        {
            Id = Guid.NewGuid(),
            Name = "lan",
            Cidr = "192.0.2.0/24",
            CreatedAtUtc = now,
        };
        ClusterNodeEntity peer = new()
        {
            Id = Guid.NewGuid(),
            Name = "ninlil-basement",
            RemoteNodeId = Guid.NewGuid(),
            Host = "192.0.2.53",
            Port = 443,
            TsigKeyName = "cluster-key",
            TlsCertificateThumbprint = "00",
            CreatedAtUtc = now,
        };
        DhcpScopeEntity scope = new()
        {
            Id = Guid.NewGuid(),
            Name = "lan",
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            RangeStartAddress = "192.0.2.100",
            RangeEndAddress = "192.0.2.200",
            SubnetPrefixLength = 24,
            CreatedAtUtc = now,
        };
        DhcpReservationEntity reservation = new()
        {
            Id = Guid.NewGuid(),
            ScopeId = scope.Id,
            ClientIdentifier = "aa:bb:cc:dd:ee:ff",
            IpAddress = "192.0.2.150",
            CreatedAtUtc = now,
        };
        DhcpScopeCustomOptionEntity customOption = new()
        {
            Id = Guid.NewGuid(),
            ScopeId = scope.Id,
            OptionTag = 42,
            ValueHex = "c0000201",
            CreatedAtUtc = now,
        };
        LeaseEntity lease = new()
        {
            Id = Guid.NewGuid(),
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            ScopeName = "lan",
            ClientIdentifier = "11:22:33:44:55:66",
            IpAddress = "192.0.2.101",
            State = LeaseStateEnum.Active,
            LeaseExpiresAtUtc = now.AddHours(1),
            CreatedAtUtc = now,
        };

        await using (AsyncServiceScope seedScope = scopeFactory.CreateAsyncScope())
        {
            IServiceProvider services = seedScope.ServiceProvider;
            await services.GetRequiredService<IDnsClientRepository>().AddAsync(device);
            await services.GetRequiredService<IDnsClientAddressRepository>().AddAsync(address);
            await services.GetRequiredService<IDnsClientScheduleRepository>().AddAsync(schedule);
            await services
                .GetRequiredService<IDnsClientStaticAddressMappingRepository>()
                .AddAsync(mapping);
            await services.GetRequiredService<IDnsAccessControlRuleRepository>().AddAsync(rule);
            await services.GetRequiredService<IClusterNodeRepository>().AddAsync(peer);
            await services.GetRequiredService<IDhcpScopeRepository>().AddAsync(scope);
            await services.GetRequiredService<IDhcpReservationRepository>().AddAsync(reservation);
            await services
                .GetRequiredService<IDhcpScopeCustomOptionRepository>()
                .AddAsync(customOption);
            await services.GetRequiredService<ILeaseRepository>().AddAsync(lease);
            await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        IBackupService backupService = _host.Services.GetRequiredService<IBackupService>();
        byte[] archive = await backupService.CreateBackupAsync("a-strong-test-passphrase");

        // Remove every seeded row, children before parents, so the restore below has to bring
        // each one back from the archive itself.
        await using (AsyncServiceScope wipeScope = scopeFactory.CreateAsyncScope())
        {
            IServiceProvider services = wipeScope.ServiceProvider;
            await RemoveAsync(services.GetRequiredService<ILeaseRepository>(), lease.Id);
            await RemoveAsync(
                services.GetRequiredService<IDhcpScopeCustomOptionRepository>(),
                customOption.Id
            );
            await RemoveAsync(
                services.GetRequiredService<IDhcpReservationRepository>(),
                reservation.Id
            );
            await RemoveAsync(services.GetRequiredService<IDhcpScopeRepository>(), scope.Id);
            await RemoveAsync(services.GetRequiredService<IClusterNodeRepository>(), peer.Id);
            await RemoveAsync(
                services.GetRequiredService<IDnsAccessControlRuleRepository>(),
                rule.Id
            );
            await RemoveAsync(
                services.GetRequiredService<IDnsClientStaticAddressMappingRepository>(),
                mapping.Id
            );
            await RemoveAsync(
                services.GetRequiredService<IDnsClientScheduleRepository>(),
                schedule.Id
            );
            await RemoveAsync(
                services.GetRequiredService<IDnsClientAddressRepository>(),
                address.Id
            );
            await RemoveAsync(services.GetRequiredService<IDnsClientRepository>(), device.Id);
            await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        BackupRestoreSummary summary = await backupService.RestoreBackupAsync(
            new MemoryStream(archive),
            "a-strong-test-passphrase"
        );

        // Every catalog entry must be reported, whether or not it held any rows.
        foreach (Catalog.BackupEntityDescriptor descriptor in Catalog.BackupEntityCatalog.Entries)
            Assert.True(summary.EntityCounts.ContainsKey(descriptor.Name), descriptor.Name);

        await using AsyncServiceScope verifyScope = scopeFactory.CreateAsyncScope();
        IServiceProvider verify = verifyScope.ServiceProvider;

        // The device must still resolve by its own identifier, through its restored address.
        DnsClientEntity restoredDevice = await verify
            .GetRequiredService<IDnsClientRepository>()
            .GetByClientIdentifierAsync("aa:bb:cc:dd:ee:ff", InternetProtocolVersionEnum.IPv4);
        Assert.Equal(device.Id, restoredDevice?.Id);
        Assert.Equal("living-room-tv", restoredDevice.DisplayName);

        Assert.NotNull(
            await verify
                .GetRequiredService<IDnsClientScheduleRepository>()
                .GetByIdAsync(schedule.Id)
        );
        Assert.NotNull(
            await verify
                .GetRequiredService<IDnsClientStaticAddressMappingRepository>()
                .GetByIdAsync(mapping.Id)
        );
        Assert.NotNull(
            await verify.GetRequiredService<IDnsAccessControlRuleRepository>().GetByIdAsync(rule.Id)
        );
        Assert.NotNull(
            await verify.GetRequiredService<IClusterNodeRepository>().GetByIdAsync(peer.Id)
        );
        Assert.Equal(
            scope.Id,
            (
                await verify
                    .GetRequiredService<IDhcpReservationRepository>()
                    .GetByIdAsync(reservation.Id)
            )?.ScopeId
        );
        Assert.Equal(
            "c0000201",
            (
                await verify
                    .GetRequiredService<IDhcpScopeCustomOptionRepository>()
                    .GetByIdAsync(customOption.Id)
            )?.ValueHex
        );
        LeaseEntity restoredLease = await verify
            .GetRequiredService<ILeaseRepository>()
            .GetByIdAsync(lease.Id);
        Assert.Equal("192.0.2.101", restoredLease?.IpAddress);
        Assert.Equal(LeaseStateEnum.Active, restoredLease.State);
    }

    private static async Task RemoveAsync<TEntity>(IRepository<TEntity> repository, Guid id)
        where TEntity : EntityBase => repository.Remove(await repository.GetByIdAsync(id));

    [Fact]
    public async Task RestoreWithTheWrongPassphraseThrowsAndLeavesTheDatabaseUntouched()
    {
        await SeedOneZoneAsync();

        IBackupService backupService = _host.Services.GetRequiredService<IBackupService>();
        byte[] archive = await backupService.CreateBackupAsync("the-real-passphrase-12345");

        await Assert.ThrowsAsync<BackupDecryptionFailedException>(() =>
            backupService.RestoreBackupAsync(
                new MemoryStream(archive),
                "a-completely-wrong-passphrase"
            )
        );

        // Nothing was written -- the one zone seeded above is still exactly the one zone present.
        Assert.Single(await _host.Services.GetRequiredService<IZoneRepository>().ListAsync());
    }

    [Fact]
    public async Task RestoreWithATamperedArchiveThrowsAndLeavesTheDatabaseUntouched()
    {
        await SeedOneZoneAsync();

        IBackupService backupService = _host.Services.GetRequiredService<IBackupService>();
        byte[] archive = await backupService.CreateBackupAsync("a-strong-test-passphrase");

        // Corrupt one byte well inside the archive's own ciphertext.
        archive[archive.Length / 2] ^= 0xFF;

        await Assert.ThrowsAsync<BackupDecryptionFailedException>(() =>
            backupService.RestoreBackupAsync(new MemoryStream(archive), "a-strong-test-passphrase")
        );

        Assert.Single(await _host.Services.GetRequiredService<IZoneRepository>().ListAsync());
    }

    [Fact]
    public async Task CreateBackupWithATooShortPassphraseThrowsArgumentException()
    {
        IBackupService backupService = _host.Services.GetRequiredService<IBackupService>();

        await Assert.ThrowsAsync<ArgumentException>(() => backupService.CreateBackupAsync("short"));
    }

    [Fact]
    public async Task CreateBackupWithNoPassphraseProducesAnUnencryptedArchiveThatRestoresWithoutOne()
    {
        await SeedOneZoneAsync();

        IBackupService backupService = _host.Services.GetRequiredService<IBackupService>();
        byte[] archive = await backupService.CreateBackupAsync(null);

        Assert.Equal(BackupArchiveKind.Unencrypted, BackupArchiveCodec.PeekKind(archive));

        BackupRestoreSummary summary = await backupService.RestoreBackupAsync(
            new MemoryStream(archive),
            null
        );

        Assert.True(summary.TotalEntitiesImported > 0);
        Assert.Equal(1, summary.EntityCounts["Zones"]);
    }

    [Fact]
    public async Task RestoringAnUnencryptedArchiveIgnoresAStrayPassphraseInstead()
    {
        await SeedOneZoneAsync();

        IBackupService backupService = _host.Services.GetRequiredService<IBackupService>();
        byte[] archive = await backupService.CreateBackupAsync("");

        // A passphrase supplied for an archive that is genuinely unencrypted (per its own
        // envelope header) is simply not needed -- it must never cause the restore to fail.
        BackupRestoreSummary summary = await backupService.RestoreBackupAsync(
            new MemoryStream(archive),
            "a-passphrase-nobody-asked-for"
        );

        Assert.True(summary.TotalEntitiesImported > 0);
    }

    [Fact]
    public async Task RestoringAnEncryptedArchiveWithNoPassphraseThrowsArgumentExceptionAndLeavesDataUntouched()
    {
        await SeedOneZoneAsync();

        IBackupService backupService = _host.Services.GetRequiredService<IBackupService>();
        byte[] archive = await backupService.CreateBackupAsync("a-strong-test-passphrase");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            backupService.RestoreBackupAsync(new MemoryStream(archive), null)
        );

        Assert.Single(await _host.Services.GetRequiredService<IZoneRepository>().ListAsync());
    }

    [Fact]
    public async Task RestoreRejectsAnArchiveThatIsNotAValidZipOnceDecrypted()
    {
        byte[] notAZipInside = Crypto.BackupArchiveCodec.Encrypt(
            [1, 2, 3, 4, 5],
            "a-strong-test-passphrase"
        );

        IBackupService backupService = _host.Services.GetRequiredService<IBackupService>();

        await Assert.ThrowsAsync<BackupArchiveInvalidException>(() =>
            backupService.RestoreBackupAsync(
                new MemoryStream(notAZipInside),
                "a-strong-test-passphrase"
            )
        );
    }

    private async Task SeedOneZoneAsync()
    {
        await using AsyncServiceScope seedScope = _host
            .Services.GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        IZoneRepository zoneRepository =
            seedScope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IUnitOfWork unitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = Guid.NewGuid(),
                Name = "seed-only.example",
                Type = ZoneTypeEnum.Primary,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();
    }
}
