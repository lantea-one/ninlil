using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up the
///     Entity Framework Core persistence layer: reading which relational provider to use from
///     configuration, registering <see cref="NinlilDnsContext" /> against it, and registering
///     every DNS repository this project implements. The DHCP repositories that also read from
///     and write to this same shared context are registered separately, by
///     <c>Ninlil.Dhcp.Data.EntityFrameworkCore.Extension.ServiceCollectionEntityFrameworkCoreExtension
///     .AddNinlilDhcpEntityFrameworkCore</c>, which a caller must invoke after this method.
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
    public static IServiceCollection AddNinlilDnsEntityFrameworkCore(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        // Bind the storage options, falling back to an all-defaults instance (SQLite, with no
        // connection string configured) when the section is missing entirely.
        StorageOptions options =
            configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
            ?? new StorageOptions();

        // When SQLite is selected and no connection string was configured, generate one
        // automatically, so a fresh installation works without the operator writing any
        // configuration at all.
        if (
            options.Engine == StorageEngineEnum.Sqlite
            && string.IsNullOrWhiteSpace(options.ConnectionString)
        )
        {
            // Store the generated database file in a "data" directory next to the application.
            string dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");

            // Ensure the data directory actually exists before SQLite tries to create a file in it.
            Directory.CreateDirectory(dataDirectory);

            // Build the auto-generated connection string.
            options.ConnectionString =
                $"Data Source={Path.Combine(dataDirectory, "ninlil-dns.sqlite3")}";
        }

        // Register the resolved options themselves, in case another part of the application
        // (for example, a health check) needs to know which engine and connection are in use.
        instance.AddSingleton(options);

        // Register the database context against whichever provider was resolved above.
        instance.AddDbContext<NinlilDnsContext>(builder => builder.UseNinlilDnsStorage(options));

        // Register the unit of work and every repository this project implements.
        instance.AddScoped<IUnitOfWork, EntityFrameworkUnitOfWork>();
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

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
