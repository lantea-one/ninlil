using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Dns.Data.Abstractions.Topology;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore.Topology;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for the Entity
///     Framework Core storage: it reads the provider from configuration, registers
///     <see cref="NinlilDnsContext" /> with it, and registers every DNS repository. The DHCP
///     repositories use the same context but are registered by
///     <c>Ninlil.Dhcp.Data.EntityFrameworkCore.Extension.ServiceCollectionEntityFrameworkCoreExtension.AddNinlilDhcpEntityFrameworkCore</c>,
///     which must be called after this method.
/// </summary>
public static class ServiceCollectionEntityFrameworkCoreExtension
{
    /// <summary>
    ///     This method registers <see cref="NinlilDnsContext" /> and every DNS-specific Entity
    ///     Framework Core repository against the relational provider selected by the
    ///     <see cref="StorageOptions.SectionName" /> configuration section.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read storage settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    /// <exception cref="Ninlil.Dns.Data.Abstractions.Exceptions.DatabaseStartupRefusedException">
    ///     Thrown when the cluster settings (<c>Storage:ReadReplica</c>, <c>Storage:PostgreSql</c>,
    ///     <c>Storage:MySql</c>, <c>Storage:SqlServer</c>) are not valid or contradict the
    ///     connection string.
    /// </exception>
    public static IServiceCollection AddNinlilDnsEntityFrameworkCore(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        // A missing section means the defaults: SQLite with no connection string.
        StorageOptions options =
            configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
            ?? new StorageOptions();

        // SQLite without a connection string gets a generated database file in a "data"
        // directory next to the application, so a fresh installation works without
        // configuration.
        if (
            options.Engine == StorageEngineEnum.Sqlite
            && string.IsNullOrWhiteSpace(options.ConnectionString)
        )
        {
            string dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");

            Directory.CreateDirectory(dataDirectory);

            options.ConnectionString =
                $"Data Source={Path.Combine(dataDirectory, "ninlil-dns.sqlite3")}";
        }

        // Registered so other code (a health check, for example) can see the engine and
        // connection in use.
        instance.AddSingleton(options);

        // One notifier and one interceptor for the process: caches subscribe to the notifier, and
        // the interceptor raises it after each committed save on any context.
        instance.AddSingleton<IEntityChangeNotifier>(provider => new EntityChangeNotifier(
            exception =>
                provider
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger<EntityChangeNotifier>()
                    .LogError(exception, "A change subscriber threw; the save is not affected.")
        ));
        instance.AddSingleton<EntityChangeSaveChangesInterceptor>();

        // The cluster settings are validated and applied to the connection string once, before any
        // database work. A bad combination throws DatabaseStartupRefusedException here, which
        // Program turns into exit code 78.
        StorageConnectionStrings composed = StorageConnectionStringComposer.Compose(options);
        instance.AddSingleton(composed);

        // The router sends a scope that asked for the replica to the read connection, once the
        // startup work is done and only when the settings describe a split.
        instance.AddNinlilStorageRouting(
            composed.Replica != null,
            TimeSpan.FromSeconds(options.ReadReplica.StickyAfterWriteSeconds)
        );

        // With a split, the monitor measures lag and schema of the replica and decides whether
        // the router may use it. The strings are the ones composed at startup: the storage
        // switch is refused while a replica is configured.
        if (composed.Replica != null)
            instance.AddNinlilReplicaHealthMonitor(_ => new RelationalReplicaHealthProbe(
                options.Engine,
                composed.Primary,
                composed.Replica
            ));

        // The storage switch changes the engine and connection string of the options in place at
        // startup. The applier replaces the content of the singleton afterwards; a scope that finds
        // the singleton out of date composes again itself, so no context uses stale strings.
        instance.AddDbContext<NinlilDnsContext>(
            (provider, builder) =>
            {
                StorageConnectionStrings strings = composed;
                if (!strings.IsComposedFrom(options))
                    strings.Replace(StorageConnectionStringComposer.Compose(options));

                // The first context of a scope fixes the intent of the whole scope, so a later
                // attempt to change it throws instead of being ignored. The principal of the
                // scope keeps a user who just wrote on the primary.
                bool replica =
                    provider
                        .GetRequiredService<StorageReadIntent>()
                        .ResolveUseReplica(
                            provider.GetRequiredService<IStorageReadRouter>(),
                            provider.GetRequiredService<StoragePrincipal>().Key
                        )
                    && strings.Replica != null;

                if (replica)
                {
                    // A replica context reads only: nothing is tracked, every write throws, and
                    // the change notifier is absent because no save can happen.
                    builder
                        .UseNinlilDnsStorage(options.Engine, strings.Replica)
                        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                        .AddInterceptors(ReadOnlyGuardInterceptor.Instance);
                    return;
                }

                builder
                    .UseNinlilDnsStorage(options.Engine, strings.Primary)
                    .AddInterceptors(
                        provider.GetRequiredService<EntityChangeSaveChangesInterceptor>()
                    );
            }
        );

        instance.AddScoped<IUnitOfWork, EntityFrameworkUnitOfWork>();
        instance.AddScoped<
            IUnitOfWorkTransactionFactory,
            EntityFrameworkUnitOfWorkTransactionFactory
        >();
        instance.AddScoped<IUserRepository, UserRepository>();
        instance.AddScoped<IRoleRepository, RoleRepository>();
        instance.AddScoped<ISessionRepository, SessionRepository>();
        instance.AddScoped<IApiTokenRepository, ApiTokenRepository>();
        instance.AddScoped<ILogRepository, LogRepository>();
        instance.AddScoped<IMetricRepository, MetricRepository>();
        instance.AddScoped<IZoneRepository, ZoneRepository>();
        instance.AddScoped<IRecordRepository, RecordRepository>();
        instance.AddScoped<ITsigKeyRepository, TsigKeyRepository>();
        instance.AddScoped<IZoneChangeJournalRepository, ZoneChangeJournalRepository>();
        instance.AddScoped<IDnssecKeyRepository, DnssecKeyRepository>();
        instance.AddScoped<
            ICloudDnsProviderCredentialRepository,
            CloudDnsProviderCredentialRepository
        >();
        instance.AddScoped<IDnsFilterListRepository, DnsFilterListRepository>();
        instance.AddScoped<IDnsFilterListEntryRepository, DnsFilterListEntryRepository>();
        instance.AddScoped<IDnsClientGroupRepository, DnsClientGroupRepository>();
        instance.AddScoped<IDnsClientRepository, DnsClientRepository>();
        instance.AddScoped<IDnsClientAddressRepository, DnsClientAddressRepository>();
        instance.AddScoped<IDnsClientGroupNetworkRepository, DnsClientGroupNetworkRepository>();
        instance.AddScoped<
            IDnsClientGroupListAssignmentRepository,
            DnsClientGroupListAssignmentRepository
        >();
        instance.AddScoped<IDnsClientListAssignmentRepository, DnsClientListAssignmentRepository>();
        instance.AddScoped<IDnsClientScheduleRepository, DnsClientScheduleRepository>();
        instance.AddScoped<IDnsClientGroupScheduleRepository, DnsClientGroupScheduleRepository>();
        instance.AddScoped<IDnsForwarderRepository, DnsForwarderRepository>();
        instance.AddScoped<IWebhookSubscriptionRepository, WebhookSubscriptionRepository>();
        instance.AddScoped<IWebhookDeliveryAttemptRepository, WebhookDeliveryAttemptRepository>();
        instance.AddScoped<IDnsRewriteRepository, DnsRewriteRepository>();
        instance.AddScoped<IDnsQueryLogRepository, DnsQueryLogRepository>();
        instance.AddScoped<IDnsAccessControlRuleRepository, DnsAccessControlRuleRepository>();
        instance.AddScoped<
            IDnsClientStaticAddressMappingRepository,
            DnsClientStaticAddressMappingRepository
        >();
        instance.AddScoped<IClusterNodeRepository, ClusterNodeRepository>();
        instance.AddScoped<
            IClusterSyncedSettingsStateRepository,
            ClusterSyncedSettingsStateRepository
        >();
        instance.AddScoped<IClusterSyncTombstoneRepository, ClusterSyncTombstoneRepository>();

        return instance;
    }
}
