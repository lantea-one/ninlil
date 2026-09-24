using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Configuration;

/// <summary>
///     This class teaches the MongoDB driver how to serialize Ninlil's entities without
///     requiring a single MongoDB-specific attribute on any of them -- both this project's own
///     DNS entities (in <c>Ninlil.Dns.Data.Abstractions</c>) and <c>Ninlil.Dhcp.Data.Abstractions</c>'s
///     own DHCP entities, mirroring the single shared model
///     <c>Ninlil.Dns.Data.EntityFrameworkCore.NinlilDnsContext</c> establishes on the relational
///     side. Keeping every entity free of driver attributes means the exact same classes are
///     equally at home being mapped by Entity Framework Core's fluent configuration; this class
///     is this project's equivalent of that fluent configuration.
/// </summary>
public static class MongoClassMapRegistrar
{
    /// <summary>
    ///     This property guards <see cref="Register" /> so that registration only happens once
    ///     per process, since the underlying driver throws if the same class map or convention
    ///     pack is registered twice.
    /// </summary>
    private static bool _isRegistered;

    /// <summary>
    ///     This property protects <see cref="_isRegistered" /> and the registration work it
    ///     guards from running concurrently on two threads at once.
    /// </summary>
    private static readonly Lock RegistrationLock = new();

    /// <summary>
    ///     This method registers every convention and class map Ninlil's entities need. It is
    ///     safe to call more than once; only the first call does any work.
    /// </summary>
    public static void Register()
    {
        // Fast-path: skip the lock entirely once registration has already happened.
        if (_isRegistered)
            return;

        // Guard the actual registration so two threads racing to start up don't both try it.
        lock (RegistrationLock)
        {
            // Check again now that we hold the lock, in case another thread just finished.
            if (_isRegistered)
                return;

            // Every entity's Id (and several other properties, such as ZoneId) is a plain
            // System.Guid. Since driver version 2.19, the driver no longer assumes a default
            // byte layout for one -- serializing a Guid throws unless a representation is
            // registered explicitly. RFC 4122's own standard byte order is the only choice that
            // reads back correctly regardless of which BSON driver or tool wrote it.
            BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

            // Store every enumeration as its name (for example, "Primary") rather than its
            // underlying number, so a document opened outside the application is still readable,
            // and ignore any extra fields a document might carry from a newer version of the
            // application than the one reading it.
            ConventionPack conventions =
            [
                new EnumRepresentationConvention(BsonType.String),
                new IgnoreExtraElementsConvention(true),
            ];

            ConventionRegistry.Register(nameof(Ninlil.Dns.Data.MongoDb), conventions, _ => true);

            // Map the Id property every entity exposes onto MongoDB's required "_id" field.
            RegisterEntity<UserEntity>();
            RegisterEntity<RoleEntity>();
            RegisterEntity<UserClaimEntity>();
            RegisterEntity<RoleClaimEntity>();
            // A session document written before IsPersistent existed lacks the field; every such
            // session was issued before browser-session sign-ins did, so it reads back as a
            // long-lived one (the same backfill the relational migrations apply).
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

            // UserRoleEntity has no Id property of its own; it is identified by the combination
            // of the user and role it links, so it is mapped without an Id property at all.
            if (!BsonClassMap.IsClassMapRegistered(typeof(UserRoleEntity)))
                BsonClassMap.RegisterClassMap<UserRoleEntity>(map => map.AutoMap());

            // We're done, remember that registration has already happened.
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
        // Skip re-registering a type that was already mapped.
        if (BsonClassMap.IsClassMapRegistered(typeof(TEntity)))
            return;

        // We're done, register the class map. AutoMap() alone is sufficient to pick up the Id
        // property as MongoDB's required "_id" field: the driver's default conventions already
        // recognize a member literally named "Id" as the identifier, and that recognition walks
        // inherited members correctly. An explicit map.MapIdProperty(entity => entity.Id) call
        // here, which earlier this file had, throws instead -- MapIdProperty's expression-based
        // overload requires the resolved member to be declared directly on TEntity itself, and
        // Id is declared on EntityBase, one level up, for every single entity this registers.
        BsonClassMap.RegisterClassMap<TEntity>(map => map.AutoMap());
    }

    /// <summary>
    ///     This method registers one entity's class map exactly like <see cref="RegisterEntity{TEntity}()" />,
    ///     then applies a per-entity adjustment on top of the automatic mapping -- for example a
    ///     default value for a field older documents do not carry.
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
