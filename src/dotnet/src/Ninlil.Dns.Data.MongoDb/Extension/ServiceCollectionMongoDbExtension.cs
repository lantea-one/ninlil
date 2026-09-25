using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.MongoDb.Configuration;
using Ninlil.Dns.Data.MongoDb.Repository;

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

        // Use the database name from the connection string, or the default if it has none.
        MongoUrl mongoUrl = MongoUrl.Create(options.ConnectionString);
        string databaseName = string.IsNullOrWhiteSpace(mongoUrl.DatabaseName)
            ? DefaultDatabaseName
            : mongoUrl.DatabaseName;

        // The client and context are thread-safe, so they are singletons.
        instance.AddSingleton(options);
        instance.AddSingleton<IMongoClient>(new MongoClient(mongoUrl));
        instance.AddSingleton(provider => new NinlilDnsMongoContext(
            provider.GetRequiredService<IMongoClient>(),
            databaseName
        ));

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

        return instance;
    }
}
