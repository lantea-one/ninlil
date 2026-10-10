using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Dns.Data.Abstractions.Topology;
using Ninlil.Dns.Data.MongoDb.Configuration;
using Ninlil.Dns.Data.MongoDb.Repository;
using Ninlil.Dns.Data.MongoDb.Routing;
using Ninlil.Dns.Data.MongoDb.Topology;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for the MongoDB
///     storage, the counterpart of <c>ServiceCollectionEntityFrameworkCoreExtension</c>. It
///     registers the DNS repositories only; the DHCP repositories use the same context but are
///     registered by
///     <c>Ninlil.Dhcp.Data.MongoDb.Extension.ServiceCollectionMongoDbExtension.AddNinlilDhcpMongoDb</c>,
///     which must be called after this method.
/// </summary>
public static class ServiceCollectionMongoDbExtension
{
    /// <summary>
    ///     This constant contains the database name used when the configured connection string
    ///     does not itself specify one.
    /// </summary>
    private const string DefaultDatabaseName = "NinlilDns";

    /// <summary>
    ///     This method registers <see cref="NinlilDnsMongoContext" /> and every MongoDB
    ///     repository against the connection selected by the
    ///     <see cref="StorageOptions.SectionName" /> configuration section.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read storage settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when no MongoDB connection string is configured; unlike SQLite there is no
    ///     default to generate.
    /// </exception>
    /// <exception cref="Ninlil.Dns.Data.Abstractions.Exceptions.DatabaseStartupRefusedException">
    ///     Thrown when the <c>Storage:MongoDb</c> or <c>Storage:ReadReplica</c> settings are not
    ///     valid or contradict the connection string.
    /// </exception>
    public static IServiceCollection AddNinlilDnsMongoDb(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        StorageOptions options =
            configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
            ?? new StorageOptions();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException(
                $"The '{StorageOptions.SectionName}:ConnectionString' configuration value is "
                    + "required when the MongoDB storage engine is selected."
            );

        // Class maps must be registered before the first document is read or written.
        MongoClassMapRegistrar.Register();

        // The replica set, write concern and journal settings are validated and applied to the
        // URL once, before any database work. A bad combination throws
        // DatabaseStartupRefusedException here, which Program turns into exit code 78.
        StorageConnectionStrings composed = MongoClientSettingsComposer.Compose(options);
        instance.AddSingleton(composed);

        // Use the database name from the connection string, or the default if it has none.
        MongoUrl mongoUrl = MongoUrl.Create(composed.Primary);
        string databaseName = string.IsNullOrWhiteSpace(mongoUrl.DatabaseName)
            ? DefaultDatabaseName
            : mongoUrl.DatabaseName;

        // The client and the two contexts are thread-safe, so they are singletons. A read
        // preference other than Primary is the read/write split of this engine: the replica
        // view reads with it, the primary context always reads the primary, and writes go to
        // the primary whatever the read preference is.
        ReadPreference replicaReadPreference =
            MongoClientSettingsComposer.ComposeReplicaReadPreference(options);
        instance.AddNinlilStorageRouting(replicaReadPreference != null);

        instance.AddSingleton(options);
        instance.AddSingleton<IMongoClient>(new MongoClient(mongoUrl));
        instance.AddSingleton(provider => new MongoContexts(
            new NinlilDnsMongoContext(
                provider.GetRequiredService<IMongoClient>(),
                databaseName,
                replicaReadPreference == null ? null : ReadPreference.Primary
            ),
            replicaReadPreference == null
                ? null
                : new NinlilDnsMongoContext(
                    provider.GetRequiredService<IMongoClient>(),
                    databaseName,
                    replicaReadPreference,
                    readOnly: true
                )
        ));

        // The first context of a scope fixes the intent of the whole scope, so a later attempt to
        // change it throws instead of being ignored.
        instance.AddScoped(provider =>
        {
            MongoContexts contexts = provider.GetRequiredService<MongoContexts>();
            bool replica = provider
                .GetRequiredService<StorageReadIntent>()
                .ResolveUseReplica(provider.GetRequiredService<IStorageReadRouter>());
            return replica && contexts.Replica != null ? contexts.Replica : contexts.Primary;
        });

        // One notifier for the process: caches subscribe to it, and every successful unit-of-work
        // save raises it (see MongoChangeTracker).
        instance.AddSingleton<IEntityChangeNotifier>(provider => new EntityChangeNotifier(
            exception =>
                provider
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger<EntityChangeNotifier>()
                    .LogError(exception, "A change subscriber threw; the save is not affected.")
        ));

        // One answer per process: can this deployment run transactions?
        instance.AddSingleton<MongoTransactionSupport>();

        // Scoped, so every repository in one request shares the same pending writes.
        instance.AddScoped<MongoChangeTracker>();
        instance.AddScoped<IUnitOfWork, MongoUnitOfWork>();

        instance.AddScoped<IUserRepository, UserMongoRepository>();
        instance.AddScoped<IRoleRepository, RoleMongoRepository>();
        instance.AddScoped<ISessionRepository, SessionMongoRepository>();
        instance.AddScoped<IApiTokenRepository, ApiTokenMongoRepository>();
        instance.AddScoped<ILogRepository, LogMongoRepository>();
        instance.AddScoped<IMetricRepository, MetricMongoRepository>();
        instance.AddScoped<IZoneRepository, ZoneMongoRepository>();
        instance.AddScoped<IRecordRepository, RecordMongoRepository>();
        instance.AddScoped<ITsigKeyRepository, TsigKeyMongoRepository>();
        instance.AddScoped<IZoneChangeJournalRepository, ZoneChangeJournalMongoRepository>();
        instance.AddScoped<IDnssecKeyRepository, DnssecKeyMongoRepository>();
        instance.AddScoped<
            ICloudDnsProviderCredentialRepository,
            CloudDnsProviderCredentialMongoRepository
        >();
        instance.AddScoped<IDnsFilterListRepository, DnsFilterListMongoRepository>();
        instance.AddScoped<IDnsFilterListEntryRepository, DnsFilterListEntryMongoRepository>();
        instance.AddScoped<IDnsClientGroupRepository, DnsClientGroupMongoRepository>();
        instance.AddScoped<IDnsClientRepository, DnsClientMongoRepository>();
        instance.AddScoped<IDnsClientAddressRepository, DnsClientAddressMongoRepository>();
        instance.AddScoped<
            IDnsClientGroupNetworkRepository,
            DnsClientGroupNetworkMongoRepository
        >();
        instance.AddScoped<
            IDnsClientGroupListAssignmentRepository,
            DnsClientGroupListAssignmentMongoRepository
        >();
        instance.AddScoped<
            IDnsClientListAssignmentRepository,
            DnsClientListAssignmentMongoRepository
        >();
        instance.AddScoped<IDnsClientScheduleRepository, DnsClientScheduleMongoRepository>();
        instance.AddScoped<
            IDnsClientGroupScheduleRepository,
            DnsClientGroupScheduleMongoRepository
        >();
        instance.AddScoped<IDnsForwarderRepository, DnsForwarderMongoRepository>();
        instance.AddScoped<IWebhookSubscriptionRepository, WebhookSubscriptionMongoRepository>();
        instance.AddScoped<
            IWebhookDeliveryAttemptRepository,
            WebhookDeliveryAttemptMongoRepository
        >();
        instance.AddScoped<IDnsRewriteRepository, DnsRewriteMongoRepository>();
        instance.AddScoped<IDnsQueryLogRepository, DnsQueryLogMongoRepository>();
        instance.AddScoped<IDnsAccessControlRuleRepository, DnsAccessControlRuleMongoRepository>();
        instance.AddScoped<
            IDnsClientStaticAddressMappingRepository,
            DnsClientStaticAddressMappingMongoRepository
        >();
        instance.AddScoped<IClusterNodeRepository, ClusterNodeMongoRepository>();
        instance.AddScoped<
            IClusterSyncedSettingsStateRepository,
            ClusterSyncedSettingsStateMongoRepository
        >();
        instance.AddScoped<IClusterSyncTombstoneRepository, ClusterSyncTombstoneMongoRepository>();

        return instance;
    }
}
