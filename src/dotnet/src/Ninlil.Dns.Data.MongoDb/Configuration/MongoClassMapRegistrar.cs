using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Configuration;

/// <summary>
///     This class tells the MongoDB driver how to serialize the DNS entities
///     (<c>Ninlil.Dns.Data.Abstractions</c>) and DHCP entities (<c>Ninlil.Dhcp.Data.Abstractions</c>)
///     without any MongoDB attributes on them, so the same classes also work with the Entity
///     Framework Core configuration. It is the MongoDB counterpart of that fluent configuration,
///     covering the same shared model as <c>NinlilDnsContext</c>.
/// </summary>
public static class MongoClassMapRegistrar
{
    /// <summary>
    ///     This field makes <see cref="Register" /> run once per process, since the driver throws
    ///     if a class map or convention pack is registered twice.
    /// </summary>
    private static bool _isRegistered;

    /// <summary>
    ///     This field keeps two threads from registering at the same time.
    /// </summary>
    private static readonly Lock RegistrationLock = new();

    /// <summary>
    ///     This method registers every convention and class map the entities need. Only the first
    ///     call does any work.
    /// </summary>
    public static void Register()
    {
        if (_isRegistered)
            return;

        lock (RegistrationLock)
        {
            if (_isRegistered)
                return;

            // Ids (and properties such as ZoneId) are Guids. The driver (2.19 and later) throws
            // when serializing a Guid unless a representation is registered. The standard RFC 4122
            // byte order reads back correctly whichever driver or tool wrote it.
            BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

            // Enums are stored by name (for example "Primary") so documents read without the
            // code, and extra fields (from a newer version, or legacy fields) are ignored.
            ConventionPack conventions =
            [
                new EnumRepresentationConvention(BsonType.String),
                new IgnoreExtraElementsConvention(true),
            ];

            ConventionRegistry.Register(nameof(Ninlil.Dns.Data.MongoDb), conventions, _ => true);

            // Each entity's Id becomes MongoDB's "_id" field.
            RegisterEntity<UserEntity>();
            RegisterEntity<RoleEntity>();
            RegisterEntity<UserClaimEntity>();
            RegisterEntity<RoleClaimEntity>();
            // A session document without IsPersistent reads as long-lived, matching the default
            // the relational migrations use for older rows.
            RegisterEntity<SessionEntity>(map =>
                map.GetMemberMap(session => session.IsPersistent).SetDefaultValue(true)
            );
            RegisterEntity<ApiTokenEntity>();
            RegisterEntity<LogEntity>();
            RegisterEntity<MetricEntity>();
            RegisterEntity<ZoneEntity>();
            RegisterEntity<RecordEntity>();
            RegisterEntity<DhcpScopeEntity>();
            RegisterEntity<DhcpReservationEntity>();
            RegisterEntity<LeaseEntity>();
            RegisterEntity<DhcpScopeCustomOptionEntity>();
            RegisterEntity<DhcpTagRuleEntity>();
            RegisterEntity<DhcpTagRuleOptionEntity>();
            RegisterEntity<TsigKeyEntity>();
            RegisterEntity<ZoneChangeJournalEntryEntity>();
            RegisterEntity<SecondaryZoneRefreshStateEntity>();
            RegisterEntity<DnssecKeyEntity>();
            RegisterEntity<CloudDnsProviderCredentialEntity>();
            RegisterEntity<DnsFilterListEntity>();
            RegisterEntity<DnsFilterListEntryEntity>();
            RegisterEntity<DnsClientGroupEntity>();
            RegisterEntity<DnsClientEntity>();
            RegisterEntity<DnsClientAddressEntity>();
            RegisterEntity<DnsClientGroupNetworkEntity>();
            RegisterEntity<DnsClientGroupListAssignmentEntity>();
            RegisterEntity<DnsClientListAssignmentEntity>();
            RegisterEntity<DnsForwarderEntity>();
            RegisterEntity<WebhookSubscriptionEntity>();
            RegisterEntity<WebhookDeliveryAttemptEntity>();
            RegisterEntity<DnsRewriteEntity>();
            RegisterEntity<DnsQueryLogEntryEntity>();
            RegisterEntity<DnsAccessControlRuleEntity>();
            RegisterEntity<DnsClientStaticAddressMappingEntity>();
            RegisterEntity<ClusterNodeEntity>();
            RegisterEntity<ClusterSyncedSettingsStateEntity>();
            RegisterEntity<ClusterSyncTombstoneEntity>();

            // UserRoleEntity has no Id; the user and role pair identifies it.
            if (!BsonClassMap.IsClassMapRegistered(typeof(UserRoleEntity)))
                BsonClassMap.RegisterClassMap<UserRoleEntity>(map => map.AutoMap());

            _isRegistered = true;
        }
    }

    /// <summary>
    ///     This method registers a class map for an <see cref="EntityBase" />-derived type,
    ///     mapping its <see cref="EntityBase.Id" /> property onto MongoDB's "_id" field.
    /// </summary>
    /// <typeparam name="TEntity">The type of entity to register.</typeparam>
    private static void RegisterEntity<TEntity>()
        where TEntity : EntityBase
    {
        if (BsonClassMap.IsClassMapRegistered(typeof(TEntity)))
            return;

        // AutoMap() maps Id to "_id" by convention, including an inherited Id. Do not call
        // map.MapIdProperty(entity => entity.Id): that overload requires the member to be
        // declared on TEntity itself and throws for Id, which EntityBase declares.
        BsonClassMap.RegisterClassMap<TEntity>(map => map.AutoMap());
    }

    /// <summary>
    ///     This method registers an entity's class map like <see cref="RegisterEntity{TEntity}()" />,
    ///     then applies an adjustment, such as a default for a field older documents lack.
    /// </summary>
    /// <param name="customize">The adjustment to apply after <c>AutoMap()</c>.</param>
    private static void RegisterEntity<TEntity>(Action<BsonClassMap<TEntity>> customize)
        where TEntity : EntityBase
    {
        if (BsonClassMap.IsClassMapRegistered(typeof(TEntity)))
            return;

        BsonClassMap.RegisterClassMap<TEntity>(map =>
        {
            map.AutoMap();
            customize(map);
        });
    }
}
