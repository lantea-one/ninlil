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
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up the
///     MongoDB persistence layer, the counterpart to
///     <c>ServiceCollectionEntityFrameworkCoreExtension</c> in the Entity Framework Core project.
///     This method registers only the DNS-specific repositories; the DHCP repositories that also
///     read from and write to this same shared context are registered separately, by
///     <c>Ninlil.Dhcp.Data.MongoDb.Extension.ServiceCollectionMongoDbExtension
///     .AddNinlilDhcpMongoDb</c>, which a caller must invoke after this method.
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
    ///     Thrown when no connection string was configured for MongoDB, since -- unlike SQLite --
    ///     there is no meaningful default to generate one from.
    /// </exception>
    public static IServiceCollection AddNinlilDnsMongoDb(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        // Bind the storage options.
        StorageOptions options =
            configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
            ?? new StorageOptions();

        // Unlike SQLite, there is no server-less fallback for MongoDB; a connection string must
        // always be supplied by the operator.
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException(
                $"The '{StorageOptions.SectionName}:ConnectionString' configuration value is "
                    + "required when the MongoDB storage engine is selected."
            );

        // Teach the MongoDB driver how to serialize Ninlil's entities before the first
        // document is ever read or written.
        MongoClassMapRegistrar.Register();

        // A MongoDB connection string carries its own database name; fall back to a sensible
        // default if the operator's connection string did not include one.
        MongoUrl mongoUrl = MongoUrl.Create(options.ConnectionString);
        string databaseName = string.IsNullOrWhiteSpace(mongoUrl.DatabaseName)
            ? DefaultDatabaseName
            : mongoUrl.DatabaseName;

        // Register the resolved options and the MongoDB client and context as singletons, since
        // both are safe to share across every request for the lifetime of the application.
        instance.AddSingleton(options);
        instance.AddSingleton<IMongoClient>(new MongoClient(mongoUrl));
        instance.AddSingleton(provider => new NinlilDnsMongoContext(
            provider.GetRequiredService<IMongoClient>(),
            databaseName
        ));

        // Register the change tracker and unit of work with a scoped lifetime, so every
        // repository used to serve a single request shares the same list of pending writes.
        instance.AddScoped<MongoChangeTracker>();
        instance.AddScoped<IUnitOfWork, MongoUnitOfWork>();

        // Register every repository this project implements.
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

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
