using System.Reflection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Exceptions;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Maintenance;

/// <summary>
///     This class is the MongoDB counterpart of the relational startup migrator. <c>Program</c>
///     calls it once at startup, before anything else reads the database. It refuses to start
///     when the database records a schema step this build does not know (a newer version has
///     upgraded it), then runs each known backfill and records the step in the
///     <c>NinlilDnsSchemaSteps</c> collection (see <see cref="MongoSchemaSteps" />). The backfills
///     are idempotent and run on every start, so a database restored from an older backup is
///     repaired again. MongoDB has no migration snapshot: the backfills can be retried safely.
/// </summary>
public static class MongoStartupGuard
{
    /// <summary>
    ///     This method checks the recorded steps, runs the backfills and records the steps.
    /// </summary>
    /// <param name="context">The MongoDB context of the configured database.</param>
    /// <param name="logger">The logger for the backfill results.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <exception cref="DatabaseStartupRefusedException">
    ///     A recorded step is unknown to this build, or a backfill failed.
    /// </exception>
    public static async Task RunAsync(
        NinlilDnsMongoContext context,
        ILogger logger,
        CancellationToken stoppingToken = default
    )
    {
        List<BsonDocument> storedSteps = await context
            .SchemaSteps.Find(FilterDefinition<BsonDocument>.Empty)
            .ToListAsync(stoppingToken);
        List<string> stored = storedSteps.Select(d => d["_id"].ToString()).ToList();

        List<string> unknown = stored
            .Where(step => !MongoSchemaSteps.Known.Contains(step))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (unknown.Count > 0)
            throw new DatabaseStartupRefusedException(
                "The database was upgraded by a newer Ninlil version (schema steps it recorded "
                    + $"that this version does not know: {string.Join(", ", unknown)}). Start "
                    + "that newer version, or restore a backup taken before the upgrade. This "
                    + "version will not start on it."
            );

        foreach (string step in MongoSchemaSteps.Known)
        {
            try
            {
                int changed = step switch
                {
                    MongoSchemaSteps.DnsClientAddressLegacyBackfill =>
                        await DnsClientAddressLegacyBackfill.RunAsync(context, stoppingToken),
                    MongoSchemaSteps.ClusterKeyLinkLegacyBackfill =>
                        await ClusterKeyLinkLegacyBackfill.RunAsync(context, stoppingToken),
                    _ => 0,
                };

                if (changed > 0)
                    logger.LogInformation(
                        "Schema step {Step} changed {Count} documents.",
                        step,
                        changed
                    );
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new DatabaseStartupRefusedException(
                    $"The MongoDB schema step {step} failed: {exception.Message} It is safe to "
                        + "retry: fix the cause and start again.",
                    exception
                );
            }

            // Upsert, so the first application stamps the time and version and later starts
            // leave the single document alone.
            await context.SchemaSteps.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", step),
                Builders<BsonDocument>
                    .Update.SetOnInsert("appliedAtUtc", DateTime.UtcNow)
                    .SetOnInsert("applicationVersion", ApplicationVersion),
                new UpdateOptions { IsUpsert = true },
                stoppingToken
            );
        }
    }

    private static string ApplicationVersion { get; } =
        typeof(MongoStartupGuard)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "unknown";
}
