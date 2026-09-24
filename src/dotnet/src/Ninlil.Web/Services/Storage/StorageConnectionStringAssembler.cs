using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Web.Models;
using Npgsql;

// Define our namespace.
namespace Ninlil.Web.Services.Storage;

/// <summary>
///     This class assembles the real ADO.NET connection string
///     <see cref="IStorageEngineSwitchService.SwitchEngineAsync" /> needs from this REST boundary's
///     own structured, per-engine fields (<see cref="StorageEngineSwitchApiRequest" />), always
///     through each provider's own official <c>*ConnectionStringBuilder</c> type -- never by
///     hand-formatting a string with interpolation -- the same "never roll your own where an
///     official, tested builder exists" posture this codebase already applies to wire formats and
///     protocol parsing elsewhere. This assembly happens only at this REST boundary
///     (<see cref="Controllers.StorageController.Switch" /> calls <see cref="Assemble" /> before
///     ever calling <see cref="IStorageEngineSwitchService" />); the service itself keeps operating
///     on a single, already-assembled <see cref="StorageEngineSwitchRequest.TargetConnectionString" />,
///     entirely unaware structured fields exist -- mirroring exactly where
///     <see cref="Controllers.EventBrokerSettingsController" /> does its own equivalent
///     structured-to-primitive assembly today.
///     <para>
///         <b>The four builder types used, and where each one actually lives.</b> All four are
///         already transitively available in this project through
///         <c>Ninlil.Dns.Data.EntityFrameworkCore</c>'s own package references -- no new
///         <c>PackageReference</c> was added for this class. <see cref="SqliteConnectionStringBuilder" />
///         (<c>Microsoft.Data.Sqlite</c>, via <c>Microsoft.EntityFrameworkCore.Sqlite</c>) and
///         <see cref="NpgsqlConnectionStringBuilder" /> (<c>Npgsql</c>, via
///         <c>Npgsql.EntityFrameworkCore.PostgreSQL</c>) match this project's own prior assumption
///         exactly. <see cref="SqlConnectionStringBuilder" /> (<c>Microsoft.Data.SqlClient</c>, via
///         <c>Microsoft.EntityFrameworkCore.SqlServer</c>) likewise matches. The MySQL/MariaDB
///         builder, however, was confirmed directly against this solution's own restored NuGet
///         packages rather than assumed: it is <c>MySqlConnector.MySqlConnectionStringBuilder</c>
///         (the <c>MySqlConnector</c> package, pulled in transitively by
///         <c>Pomelo.EntityFrameworkCore.MySql</c>), not a type living directly in the Pomelo
///         package itself.
///     </para>
///     <para>
///         <b>Each engine's own <c>EncryptionMode</c> values.</b> PostgreSQL accepts any of
///         <see cref="Npgsql.SslMode" />'s own real values (<c>Disable</c>, <c>Allow</c>,
///         <c>Prefer</c>, <c>Require</c>, <c>VerifyCA</c>, <c>VerifyFull</c>). MySQL accepts any of
///         <see cref="MySqlConnector.MySqlSslMode" />'s own real values (<c>None</c>,
///         <c>Disabled</c>, <c>Preferred</c>, <c>Required</c>, <c>VerifyCA</c>, <c>VerifyFull</c>).
///         SQL Server accepts <c>Optional</c>, <c>Mandatory</c>, or <c>Strict</c> --
///         <see cref="SqlConnectionEncryptOption" /> is not a plain C# <see langword="enum" /> (a
///         value-type wrapper with implicit <see langword="bool" /> conversions instead, confirmed
///         directly by reflecting over its real members), so this class validates it via that
///         type's own <see cref="SqlConnectionEncryptOption.TryParse" /> rather than
///         <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)" />, which does not apply to it.
///     </para>
/// </summary>
public static class StorageConnectionStringAssembler
{
    /// <summary>
    ///     This method builds the real connection string
    ///     <see cref="IStorageEngineSwitchService.SwitchEngineAsync" /> needs, reading only the
    ///     structured-field section of <paramref name="request" /> that matches its own
    ///     <see cref="StorageEngineSwitchApiRequest.TargetEngine" />. A
    ///     <see cref="StorageEngineEnum.MongoDb" /> target has no structured-field section here at
    ///     all -- this method returns an empty string for it unvalidated, since
    ///     <see cref="IStorageEngineSwitchService.SwitchEngineAsync" /> itself already rejects that
    ///     target with its own clear, dedicated message before this connection string would ever be
    ///     used.
    /// </summary>
    /// <exception cref="ValidationException">
    ///     Thrown when the section matching <see cref="StorageEngineSwitchApiRequest.TargetEngine" />
    ///     is missing entirely, or one of its own required fields is missing, out of range, or --
    ///     for <c>encryptionMode</c> -- not one of that specific engine's own recognized values.
    /// </exception>
    public static string Assemble(StorageEngineSwitchApiRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.TargetEngine switch
        {
            StorageEngineEnum.Sqlite => AssembleSqlite(request.Sqlite),
            StorageEngineEnum.PostgreSql => AssemblePostgreSql(request.PostgreSql),
            StorageEngineEnum.MySql => AssembleMySql(request.MySql),
            StorageEngineEnum.SqlServer => AssembleSqlServer(request.SqlServer),
            _ => string.Empty,
        };
    }

    /// <summary>
    ///     This method builds a SQLite connection string via
    ///     <see cref="SqliteConnectionStringBuilder" /> from a single required file path.
    /// </summary>
    private static string AssembleSqlite(SqliteStorageConnectionFields fields)
    {
        if (fields is null || string.IsNullOrWhiteSpace(fields.FilePath))
            throw new ValidationException("A SQLite file path is required.");

        return new SqliteConnectionStringBuilder { DataSource = fields.FilePath }.ConnectionString;
    }

    /// <summary>
    ///     This method builds a PostgreSQL connection string via
    ///     <see cref="NpgsqlConnectionStringBuilder" />, validating <c>EncryptionMode</c> against
    ///     Npgsql's own real <see cref="Npgsql.SslMode" /> values.
    /// </summary>
    private static string AssemblePostgreSql(RelationalStorageConnectionFields fields)
    {
        RelationalFields validated = ValidateCommonFields(fields, "PostgreSql");

        if (!Enum.TryParse(validated.EncryptionMode, ignoreCase: true, out SslMode sslMode))
            throw new ValidationException(
                $"'{validated.EncryptionMode}' is not a valid PostgreSQL SSL mode. Expected one "
                    + $"of: {string.Join(", ", Enum.GetNames<SslMode>())}."
            );

        return new NpgsqlConnectionStringBuilder
        {
            Host = validated.Host,
            Port = validated.Port,
            Database = validated.DatabaseName,
            Username = validated.UserName,
            Password = validated.Password,
            SslMode = sslMode,
        }.ConnectionString;
    }

    /// <summary>
    ///     This method builds a MySQL/MariaDB connection string via
    ///     <see cref="MySqlConnector.MySqlConnectionStringBuilder" />, validating
    ///     <c>EncryptionMode</c> against that driver's own real
    ///     <see cref="MySqlConnector.MySqlSslMode" /> values.
    /// </summary>
    private static string AssembleMySql(RelationalStorageConnectionFields fields)
    {
        RelationalFields validated = ValidateCommonFields(fields, "MySql");

        if (!Enum.TryParse(validated.EncryptionMode, ignoreCase: true, out MySqlSslMode sslMode))
            throw new ValidationException(
                $"'{validated.EncryptionMode}' is not a valid MySQL SSL mode. Expected one of: "
                    + $"{string.Join(", ", Enum.GetNames<MySqlSslMode>())}."
            );

        return new MySqlConnectionStringBuilder
        {
            Server = validated.Host,
            Port = (uint)validated.Port,
            Database = validated.DatabaseName,
            UserID = validated.UserName,
            Password = validated.Password,
            SslMode = sslMode,
        }.ConnectionString;
    }

    /// <summary>
    ///     This method builds a SQL Server connection string via
    ///     <see cref="SqlConnectionStringBuilder" />, validating <c>EncryptionMode</c> against
    ///     <see cref="SqlConnectionEncryptOption" />'s own <c>TryParse</c> (<c>Optional</c>,
    ///     <c>Mandatory</c>, or <c>Strict</c>), since that type is not a plain
    ///     <see langword="enum" /> <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)" />
    ///     could validate against directly.
    /// </summary>
    private static string AssembleSqlServer(RelationalStorageConnectionFields fields)
    {
        RelationalFields validated = ValidateCommonFields(fields, "SqlServer");

        if (!SqlConnectionEncryptOption.TryParse(validated.EncryptionMode, out var encrypt))
            throw new ValidationException(
                $"'{validated.EncryptionMode}' is not a valid SQL Server encryption mode. "
                    + "Expected one of: Optional, Mandatory, Strict."
            );

        return new SqlConnectionStringBuilder
        {
            DataSource = $"{validated.Host},{validated.Port}",
            InitialCatalog = validated.DatabaseName,
            UserID = validated.UserName,
            Password = validated.Password,
            Encrypt = encrypt,
        }.ConnectionString;
    }

    /// <summary>
    ///     This method validates every field <see cref="RelationalStorageConnectionFields" />
    ///     shares across all three server-based engines (everything except
    ///     <c>EncryptionMode</c>, which each engine's own caller validates against its own
    ///     distinct real values), throwing a specific, actionable <see cref="ValidationException" />
    ///     for the first problem found -- <paramref name="fields" /> being missing entirely
    ///     included, since a caller naming, say, <see cref="StorageEngineEnum.PostgreSql" /> as its
    ///     target while leaving <see cref="StorageEngineSwitchApiRequest.PostgreSql" /> unset is
    ///     just as much a malformed request as one with a blank field inside it.
    /// </summary>
    private static RelationalFields ValidateCommonFields(
        RelationalStorageConnectionFields fields,
        string engineLabel
    )
    {
        if (fields is null)
            throw new ValidationException(
                $"'{engineLabel}' connection fields are required when targeting that engine."
            );

        if (string.IsNullOrWhiteSpace(fields.Host))
            throw new ValidationException("A host is required.");

        if (fields.Port is < 1 or > 65535)
            throw new ValidationException("Port must be between 1 and 65535.");

        if (string.IsNullOrWhiteSpace(fields.DatabaseName))
            throw new ValidationException("A database name is required.");

        if (string.IsNullOrWhiteSpace(fields.UserName))
            throw new ValidationException("A username is required.");

        return new RelationalFields(
            fields.Host,
            fields.Port,
            fields.DatabaseName,
            fields.UserName,
            fields.Password ?? string.Empty,
            fields.EncryptionMode
        );
    }

    /// <summary>
    ///     This record carries <see cref="RelationalStorageConnectionFields" />'s own fields once
    ///     <see cref="ValidateCommonFields" /> has already confirmed every one of them except
    ///     <see cref="EncryptionMode" /> is present and well-formed, with <see cref="Password" />
    ///     normalized from a possible <see langword="null" /> to an empty string -- sparing every
    ///     per-engine assembly method from repeating that same null-guard.
    /// </summary>
    private sealed record RelationalFields(
        string Host,
        int Port,
        string DatabaseName,
        string UserName,
        string Password,
        string EncryptionMode
    );
}
