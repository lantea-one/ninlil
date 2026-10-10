using MongoDB.Driver;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Topology;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Topology;

/// <summary>
///     This static class applies <see cref="StorageMongoDbOptions" /> to the MongoDB connection
///     URL with <see cref="MongoUrlBuilder" />: the replica set name, the write concern and the
///     journal flag. A URL that needs no change is returned exactly as the operator wrote it. A
///     setting that the URL also holds with another value is refused, naming both values. The
///     URL's read preference stays the one for work that needs the primary; the read preference
///     for replica-tolerant work comes from <see cref="ComposeReplicaReadPreference" />.
/// </summary>
public static class MongoClientSettingsComposer
{
    /// <summary>
    ///     This constant contains the lowest <c>maxStalenessSeconds</c> the driver accepts.
    /// </summary>
    public const int MinimumMaxStalenessSeconds = 90;

    /// <summary>
    ///     This method composes the connection URL for the engine in <paramref name="options" />.
    /// </summary>
    /// <param name="options">The storage configuration; the engine must be MongoDB.</param>
    /// <returns>The composed strings; <see cref="StorageConnectionStrings.Replica" /> is null.</returns>
    /// <exception cref="DatabaseStartupRefusedException">
    ///     Thrown when the settings contradict each other or the URL.
    /// </exception>
    public static StorageConnectionStrings Compose(StorageOptions options)
    {
        List<string> warnings = StorageTopologyValidator.Validate(options);
        StorageMongoDbOptions mongo = options.MongoDb ?? new StorageMongoDbOptions();
        string source = options.ConnectionString;

        // Parse first: the replica read preference check below also reads the URL.
        MongoUrlBuilder builder;
        try
        {
            builder = new MongoUrlBuilder(source);
        }
        catch (Exception exception)
            when (exception is MongoConfigurationException or ArgumentException or FormatException)
        {
            throw StorageTopologyValidator.Refusal(
                "Storage:ConnectionString is not a valid MongoDB connection string."
            );
        }

        bool changed = false;

        if (!string.IsNullOrWhiteSpace(mongo.ReplicaSet))
        {
            string name = mongo.ReplicaSet.Trim();
            if (
                !string.IsNullOrEmpty(builder.ReplicaSetName)
                && !string.Equals(builder.ReplicaSetName, name, StringComparison.Ordinal)
            )
                throw Refusal(
                    nameof(StorageMongoDbOptions.ReplicaSet),
                    $"is '{name}', but the connection string says '{builder.ReplicaSetName}'. "
                        + "Set it in one place only."
                );
            if (string.IsNullOrEmpty(builder.ReplicaSetName))
            {
                builder.ReplicaSetName = name;
                changed = true;
            }
        }

        // The write concern of the URL, when it has one, is the "w" value; the option must agree.
        WriteConcern.WValue optionW = ParseWriteConcern(mongo.WriteConcern);
        WriteConcern.WValue urlW = builder.W;
        if (optionW is not null && urlW is not null && !Same(optionW, urlW))
            throw Refusal(
                nameof(StorageMongoDbOptions.WriteConcern),
                $"is '{Describe(optionW)}', but the connection string says '{Describe(urlW)}'. "
                    + "Set it in one place only."
            );

        // A replica set acknowledges writes from a majority unless the operator says otherwise,
        // so that a write survives the loss of the primary.
        WriteConcern.WValue wanted =
            optionW
            ?? (
                urlW is null && !string.IsNullOrEmpty(builder.ReplicaSetName)
                    ? WriteConcern.WMode.Majority
                    : null
            );
        bool writeConcernApplied = wanted is not null && urlW is null;
        if (writeConcernApplied)
        {
            builder.W = wanted;
            changed = true;
        }

        if (mongo.Journal.HasValue)
        {
            if (builder.Journal.HasValue && builder.Journal.Value != mongo.Journal.Value)
                throw Refusal(
                    nameof(StorageMongoDbOptions.Journal),
                    $"is '{mongo.Journal.Value.ToString().ToLowerInvariant()}', but the connection string "
                        + $"says '{builder.Journal.Value.ToString().ToLowerInvariant()}'. Set it in one place only."
                );
            if (!builder.Journal.HasValue)
            {
                builder.Journal = mongo.Journal.Value;
                changed = true;
            }
        }
        else if (writeConcernApplied && !builder.Journal.HasValue)
        {
            builder.Journal = true;
            changed = true;
        }

        ValidateReadPreference(mongo, builder, warnings);

        string primary = changed ? builder.ToString() : source;
        return new StorageConnectionStrings(
            StorageEngineEnum.MongoDb,
            source,
            primary,
            null,
            warnings
        );
    }

    /// <summary>
    ///     This method builds the read preference for work that tolerates replica lag, from
    ///     <see cref="StorageMongoDbOptions.ReadPreference" /> and
    ///     <see cref="StorageMongoDbOptions.MaxStalenessSeconds" />.
    /// </summary>
    /// <param name="options">The storage configuration.</param>
    /// <returns>
    ///     The read preference, or null when none is configured or it is <c>Primary</c>, meaning
    ///     there is no split.
    /// </returns>
    /// <exception cref="DatabaseStartupRefusedException">Thrown for an unknown mode or a staleness below the driver minimum.</exception>
    public static ReadPreference ComposeReplicaReadPreference(StorageOptions options)
    {
        StorageMongoDbOptions mongo = options.MongoDb ?? new StorageMongoDbOptions();
        ReadPreferenceMode? mode = ParseMode(mongo.ReadPreference);
        if (mode is null or ReadPreferenceMode.Primary)
            return null;

        TimeSpan? staleness =
            mongo.MaxStalenessSeconds == 0 ? null : TimeSpan.FromSeconds(mongo.MaxStalenessSeconds);
        return new ReadPreference(mode.Value, maxStaleness: staleness);
    }

    /// <summary>
    ///     This method checks the read preference settings against each other and the URL.
    /// </summary>
    private static void ValidateReadPreference(
        StorageMongoDbOptions mongo,
        MongoUrlBuilder builder,
        List<string> warnings
    )
    {
        ReadPreferenceMode? mode = ParseMode(mongo.ReadPreference);

        if (mongo.MaxStalenessSeconds < 0)
            throw Refusal(
                nameof(StorageMongoDbOptions.MaxStalenessSeconds),
                $"is {mongo.MaxStalenessSeconds}, but must not be negative."
            );
        if (mongo.MaxStalenessSeconds is > 0 and < MinimumMaxStalenessSeconds)
            throw Refusal(
                nameof(StorageMongoDbOptions.MaxStalenessSeconds),
                $"is {mongo.MaxStalenessSeconds}, but the driver needs 0 (unset) or at least "
                    + $"{MinimumMaxStalenessSeconds}."
            );
        if (mongo.MaxStalenessSeconds > 0 && (mode is null or ReadPreferenceMode.Primary))
            warnings.Add(
                "Storage:MongoDb:MaxStalenessSeconds is set but the read preference is Primary; it is ignored."
            );

        ReadPreference urlPreference = builder.ReadPreference;
        if (
            mode.HasValue
            && urlPreference is not null
            && urlPreference.ReadPreferenceMode != mode.Value
        )
            throw Refusal(
                nameof(StorageMongoDbOptions.ReadPreference),
                $"is '{mode.Value}', but the connection string says '{urlPreference.ReadPreferenceMode}'. "
                    + "Set it in one place only; the connection string's value applies to work that "
                    + "needs the primary."
            );
    }

    /// <summary>
    ///     This method parses a read preference name, or returns null for an empty value.
    /// </summary>
    private static ReadPreferenceMode? ParseMode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (
            Enum.TryParse(value.Trim(), true, out ReadPreferenceMode mode)
            && Enum.IsDefined(mode)
            && !int.TryParse(value, out _)
        )
            return mode;
        throw Refusal(
            nameof(StorageMongoDbOptions.ReadPreference),
            $"'{value}' is not one of Primary, PrimaryPreferred, Secondary, SecondaryPreferred or Nearest."
        );
    }

    /// <summary>
    ///     This method parses the write concern setting, or returns null for an empty value.
    /// </summary>
    private static WriteConcern.WValue ParseWriteConcern(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        string text = value.Trim();
        if (string.Equals(text, "Majority", StringComparison.OrdinalIgnoreCase))
            return WriteConcern.WMode.Majority;
        if (string.Equals(text, "Acknowledged", StringComparison.OrdinalIgnoreCase))
            return new WriteConcern.WCount(1);
        if (text.Length == 2 && (text[0] is 'W' or 'w') && text[1] is >= '2' and <= '9')
            return new WriteConcern.WCount(text[1] - '0');
        throw Refusal(
            nameof(StorageMongoDbOptions.WriteConcern),
            $"'{value}' is not one of Majority, Acknowledged or W2 to W9."
        );
    }

    private static bool Same(WriteConcern.WValue a, WriteConcern.WValue b) =>
        string.Equals(Describe(a), Describe(b), StringComparison.OrdinalIgnoreCase);

    private static string Describe(WriteConcern.WValue value) =>
        value switch
        {
            WriteConcern.WCount count => count.Value.ToString(),
            WriteConcern.WMode mode => mode.Value,
            _ => value.ToString(),
        };

    private static DatabaseStartupRefusedException Refusal(string setting, string message) =>
        StorageTopologyValidator.Refusal(
            $"{StorageTopologyValidator.Key(nameof(StorageOptions.MongoDb), setting)} {message}"
        );
}
