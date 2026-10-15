using System.Diagnostics;
using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Maintenance;

/// <summary>
///     This class is the one list of MongoDB indexes Ninlil needs, and the code that creates
///     them. <see cref="MongoStartupGuard" /> calls <see cref="EnsureAsync" /> once at startup,
///     before anything reads the database, so no lookup is a collection scan. The relational
///     model has the same indexes (see the configurations in
///     <c>Ninlil.Dns.Data.EntityFrameworkCore/Configuration</c>); each index here serves the
///     filter or sort of the repository method named next to it.
/// </summary>
/// <remarks>
///     Every index is non-unique: the relational unique constraints are not mirrored (a database
///     that already holds duplicates must still start). Index creation is idempotent, so every
///     start runs it. Indexes are performance-only, so an older build ignores the extra ones and
///     a failure to create one is logged and does not stop the start.
/// </remarks>
public static class MongoIndexCatalog
{
    /// <summary>
    ///     This method creates every index of every collection and drops the single-field query
    ///     log indexes that the composite ones replace.
    /// </summary>
    /// <param name="context">The MongoDB context of the configured database.</param>
    /// <param name="logger">The logger for the per-collection and total timings.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public static async Task EnsureAsync(
        NinlilDnsMongoContext context,
        ILogger logger,
        CancellationToken stoppingToken = default
    )
    {
        // Creating and dropping indexes is a schema change: primary only.
        context.EnsureWritable("The MongoDB index setup");

        Stopwatch total = Stopwatch.StartNew();
        int created = 0;

        // Identity.
        created += await EnsureCollectionAsync(
            context.Users,
            new IndexSet<UserEntity>()
                .Asc(user => user.NormalizedUserName)
                .Asc(user => user.NormalizedEmail)
                .Asc(user => user.ExternalIdentityProvider, user => user.ExternalSubjectId),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.Roles,
            new IndexSet<RoleEntity>().Asc(role => role.NormalizedName),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.UserRoles,
            new IndexSet<UserRoleEntity>().Asc(link => link.UserId).Asc(link => link.RoleId),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.UserClaims,
            new IndexSet<UserClaimEntity>()
                .Asc(claim => claim.UserId)
                .Asc(claim => claim.ClaimType, claim => claim.ClaimValue),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.RoleClaims,
            new IndexSet<RoleClaimEntity>().Asc(claim => claim.RoleId),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.Sessions,
            new IndexSet<SessionEntity>()
                .Asc(session => session.RefreshTokenHash)
                .Asc(session => session.UserId),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.ApiTokens,
            new IndexSet<ApiTokenEntity>().Asc(token => token.TokenHash).Asc(token => token.UserId),
            logger,
            stoppingToken
        );

        // Logs and metrics (time-range reads, newest first for logs).
        created += await EnsureCollectionAsync(
            context.Logs,
            new IndexSet<LogEntity>().Desc(log => log.TimestampUtc),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.Metrics,
            new IndexSet<MetricEntity>().Asc(
                metric => metric.Category,
                metric => metric.Name,
                metric => metric.TimestampUtc
            ),
            logger,
            stoppingToken
        );

        // Zones and records.
        created += await EnsureCollectionAsync(
            context.Zones,
            new IndexSet<ZoneEntity>().Asc(zone => zone.Name),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.Records,
            new IndexSet<RecordEntity>()
                .Asc(record => record.ZoneId, record => record.Name, record => record.Type)
                .Asc(record => record.ManagedByFeature, record => record.Type),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.TsigKeys,
            new IndexSet<TsigKeyEntity>().Asc(key => key.Name),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.DnssecKeys,
            new IndexSet<DnssecKeyEntity>().Asc(key => key.ZoneId),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.ZoneChangeJournalEntries,
            new IndexSet<ZoneChangeJournalEntryEntity>().Asc(
                entry => entry.ZoneId,
                entry => entry.OldSerial
            ),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.SecondaryZoneRefreshStates,
            // One row per zone; a second row for the same zone must fail to save.
            new IndexSet<SecondaryZoneRefreshStateEntity>().Unique(state => state.ZoneId),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.CloudDnsProviderCredentials,
            new IndexSet<CloudDnsProviderCredentialEntity>().Asc(credential => credential.Name),
            logger,
            stoppingToken
        );

        // Filtering, clients and schedules.
        created += await EnsureCollectionAsync(
            context.DnsFilterLists,
            new IndexSet<DnsFilterListEntity>().Asc(list => list.Name),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.DnsFilterListEntries,
            new IndexSet<DnsFilterListEntryEntity>().Asc(
                entry => entry.ListId,
                entry => entry.Value
            ),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.DnsClientGroups,
            new IndexSet<DnsClientGroupEntity>().Asc(group => group.Name),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.DnsClients,
            new IndexSet<DnsClientEntity>()
                .Asc(client => client.ClientGroupId)
                .Asc(client => client.CreatedAtUtc, client => client.Id),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.DnsClientAddresses,
            new IndexSet<DnsClientAddressEntity>()
                .Asc(address => address.ClientIdentifier, address => address.ProtocolVersion)
                .Asc(address => address.ClientId)
                .Asc(address => address.LastSeenUtc),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.DnsClientGroupNetworks,
            new IndexSet<DnsClientGroupNetworkEntity>().Asc(network => network.ClientGroupId),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.DnsClientGroupListAssignments,
            new IndexSet<DnsClientGroupListAssignmentEntity>()
                .Asc(assignment => assignment.ClientGroupId)
                .Asc(assignment => assignment.ListId),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.DnsClientListAssignments,
            new IndexSet<DnsClientListAssignmentEntity>()
                .Asc(assignment => assignment.ClientId)
                .Asc(assignment => assignment.ListId),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.DnsClientSchedules,
            new IndexSet<DnsClientScheduleEntity>().Asc(schedule => schedule.ClientId),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.DnsClientGroupSchedules,
            new IndexSet<DnsClientGroupScheduleEntity>().Asc(schedule => schedule.ClientGroupId),
            logger,
            stoppingToken
        );

        // Query log: filter on one column, newest first. The name column has no index on
        // purpose: its only filter is an unanchored case-insensitive regex, which cannot seek.
        // The single-field indexes earlier builds created are replaced by these.
        created += await EnsureCollectionAsync(
            context.DnsQueryLogEntries,
            new IndexSet<DnsQueryLogEntryEntity>()
                .Desc(entry => entry.OccurredAtUtc)
                .AscDesc(entry => entry.ClientAddress, entry => entry.OccurredAtUtc)
                .AscDesc(entry => entry.ClientIdentifier, entry => entry.OccurredAtUtc)
                .AscDesc(entry => entry.ResponseCode, entry => entry.OccurredAtUtc),
            logger,
            stoppingToken,
            "QueriedName_1",
            "ClientAddress_1",
            "ClientIdentifier_1",
            "ResponseCode_1"
        );

        // Webhooks: the retry queue, retention, and one subscription's attempts newest first (the index ends in
        // the id so the page order, which breaks ties on the id, is read straight off the index).
        created += await EnsureCollectionAsync(
            context.WebhookDeliveryAttempts,
            new IndexSet<WebhookDeliveryAttemptEntity>()
                .Asc(attempt => attempt.Status, attempt => attempt.NextRetryDueUtc)
                .Asc(attempt => attempt.CompletedAtUtc)
                .Asc(attempt => attempt.SubscriptionId)
                .AscDescDesc(
                    attempt => attempt.SubscriptionId,
                    attempt => attempt.CreatedAtUtc,
                    attempt => attempt.Id
                ),
            logger,
            stoppingToken,
            "SubscriptionId_1_CreatedAtUtc_-1"
        );
        created += await EnsureCollectionAsync(
            context.WebhookSubscriptions,
            new IndexSet<WebhookSubscriptionEntity>().Asc(subscription => subscription.Name),
            logger,
            stoppingToken
        );

        // DHCP.
        created += await EnsureCollectionAsync(
            context.DhcpScopes,
            new IndexSet<DhcpScopeEntity>().Asc(scope => scope.Name),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.Leases,
            new IndexSet<LeaseEntity>()
                .Asc(lease => lease.ScopeName, lease => lease.ClientIdentifier)
                .Asc(lease => lease.ScopeName, lease => lease.IpAddress)
                .Asc(lease => lease.IpAddress)
                .Asc(lease => lease.State, lease => lease.LeaseExpiresAtUtc),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.DhcpReservations,
            new IndexSet<DhcpReservationEntity>()
                .Asc(
                    reservation => reservation.ScopeId,
                    reservation => reservation.ClientIdentifier
                )
                .Asc(reservation => reservation.ScopeId, reservation => reservation.IpAddress),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.DhcpScopeCustomOptions,
            new IndexSet<DhcpScopeCustomOptionEntity>().Asc(
                option => option.ScopeId,
                option => option.OptionTag
            ),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.DhcpTagRuleOptions,
            new IndexSet<DhcpTagRuleOptionEntity>().Asc(
                option => option.TagRuleId,
                option => option.OptionTag
            ),
            logger,
            stoppingToken
        );

        // Cluster.
        created += await EnsureCollectionAsync(
            context.ClusterNodes,
            new IndexSet<ClusterNodeEntity>().Asc(node => node.RemoteNodeId),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.ClusterSyncTombstones,
            new IndexSet<ClusterSyncTombstoneEntity>().Asc(
                tombstone => tombstone.EntityType,
                tombstone => tombstone.EntityKey
            ),
            logger,
            stoppingToken
        );
        created += await EnsureCollectionAsync(
            context.ClusterSyncedSettingsStates,
            new IndexSet<ClusterSyncedSettingsStateEntity>().Asc(state => state.SectionName),
            logger,
            stoppingToken
        );

        logger.LogInformation(
            "MongoDB index setup finished in {ElapsedMilliseconds} ms ({Count} indexes ensured).",
            total.ElapsedMilliseconds,
            created
        );
    }

    /// <summary>
    ///     This method creates one collection's indexes in a single command, then drops the named
    ///     superseded indexes. A failure is logged and does not propagate (see the class remarks).
    /// </summary>
    /// <returns>The number of indexes this collection should have, or zero after a failure.</returns>
    private static async Task<int> EnsureCollectionAsync<T>(
        IMongoCollection<T> collection,
        IndexSet<T> indexes,
        ILogger logger,
        CancellationToken stoppingToken,
        params string[] supersededIndexNames
    )
    {
        string name = collection.CollectionNamespace.CollectionName;
        Stopwatch watch = Stopwatch.StartNew();

        try
        {
            await collection.Indexes.CreateManyAsync(indexes.Models, stoppingToken);

            foreach (string superseded in supersededIndexNames)
            {
                try
                {
                    await collection.Indexes.DropOneAsync(superseded, stoppingToken);
                }
                catch (MongoCommandException exception)
                    when (exception.CodeName is "IndexNotFound" or "NamespaceNotFound")
                {
                    // Already gone, which is the normal case after the first start.
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Could not create the indexes of collection {Collection}; queries on it stay "
                    + "correct but may be slow.",
                name
            );
            return 0;
        }

        logger.LogDebug(
            "Ensured {Count} indexes on {Collection} in {ElapsedMilliseconds} ms.",
            indexes.Models.Count,
            name,
            watch.ElapsedMilliseconds
        );
        return indexes.Models.Count;
    }

    /// <summary>
    ///     This class collects the index definitions of one collection (non-unique, except those added with
    ///     <see cref="Unique" />). Field
    ///     expressions are checked by the compiler against the entity, and the driver maps them
    ///     to the stored field names (the same mapping the repositories' filters use).
    /// </summary>
    /// <typeparam name="T">The entity type of the collection.</typeparam>
    private sealed class IndexSet<T>
    {
        /// <summary>
        ///     This property contains the index definitions added so far.
        /// </summary>
        public List<CreateIndexModel<T>> Models { get; } = [];

        /// <summary>
        ///     This method adds an index on the fields in ascending order.
        /// </summary>
        public IndexSet<T> Asc(params Expression<Func<T, object>>[] fields)
        {
            Models.Add(
                new CreateIndexModel<T>(
                    Builders<T>.IndexKeys.Combine(
                        fields.Select(field => Builders<T>.IndexKeys.Ascending(field))
                    )
                )
            );
            return this;
        }

        /// <summary>
        ///     This method adds a unique index on one field in ascending order. The server
        ///     rejects a second document with the same value.
        /// </summary>
        public IndexSet<T> Unique(Expression<Func<T, object>> field)
        {
            Models.Add(
                new CreateIndexModel<T>(
                    Builders<T>.IndexKeys.Ascending(field),
                    new CreateIndexOptions { Unique = true }
                )
            );
            return this;
        }

        /// <summary>
        ///     This method adds an index on one field in descending order.
        /// </summary>
        public IndexSet<T> Desc(Expression<Func<T, object>> field)
        {
            Models.Add(new CreateIndexModel<T>(Builders<T>.IndexKeys.Descending(field)));
            return this;
        }

        /// <summary>
        ///     This method adds a compound index: <paramref name="equalityField" /> ascending, then
        ///     <paramref name="newestFirstField" /> descending. It serves an equality filter with
        ///     the newest rows first.
        /// </summary>
        public IndexSet<T> AscDesc(
            Expression<Func<T, object>> equalityField,
            Expression<Func<T, object>> newestFirstField
        )
        {
            Models.Add(
                new CreateIndexModel<T>(
                    Builders<T>.IndexKeys.Ascending(equalityField).Descending(newestFirstField)
                )
            );
            return this;
        }

        /// <summary>
        ///     This method adds a compound index: <paramref name="equalityField" /> ascending, then
        ///     <paramref name="newestFirstField" /> and <paramref name="tieBreakField" />
        ///     descending. It serves an equality filter with the newest rows first and a
        ///     deterministic tie-break.
        /// </summary>
        public IndexSet<T> AscDescDesc(
            Expression<Func<T, object>> equalityField,
            Expression<Func<T, object>> newestFirstField,
            Expression<Func<T, object>> tieBreakField
        )
        {
            Models.Add(
                new CreateIndexModel<T>(
                    Builders<T>
                        .IndexKeys.Ascending(equalityField)
                        .Descending(newestFirstField)
                        .Descending(tieBreakField)
                )
            );
            return this;
        }
    }
}
