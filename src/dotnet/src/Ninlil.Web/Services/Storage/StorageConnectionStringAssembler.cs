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
///     This class builds the ADO.NET connection string for
///     <see cref="IStorageEngineSwitchService.ScheduleSwitchAsync" /> from the structured fields of
///     <see cref="StorageEngineSwitchApiRequest" />, always through the provider's
///     <c>*ConnectionStringBuilder</c> rather than string interpolation, so values are escaped
///     correctly. <see cref="Controllers.StorageController.Switch" /> calls <see cref="Assemble" />;
///     the service itself only sees the finished
///     <see cref="StorageEngineSwitchRequest.TargetConnectionString" />.
///     <para>
///         <b>The builders.</b> All four come through the package references of
///         <c>Ninlil.Dns.Data.EntityFrameworkCore</c>: <see cref="SqliteConnectionStringBuilder" />
///         (<c>Microsoft.Data.Sqlite</c>), <see cref="NpgsqlConnectionStringBuilder" />
///         (<c>Npgsql</c>), <see cref="SqlConnectionStringBuilder" />
///         (<c>Microsoft.Data.SqlClient</c>) and <c>MySqlConnector.MySqlConnectionStringBuilder</c>,
///         which comes from the <c>MySqlConnector</c> package that
///         <c>Pomelo.EntityFrameworkCore.MySql</c> depends on, not from Pomelo itself.
///     </para>
///     <para>
///         <b>Accepted <c>EncryptionMode</c> values.</b> PostgreSQL: a
///         <see cref="Npgsql.SslMode" /> name, case ignored (<c>Disable</c>, <c>Allow</c>,
///         <c>Prefer</c>, <c>Require</c>, <c>VerifyCA</c>, <c>VerifyFull</c>). MySQL: a
///         <see cref="MySqlConnector.MySqlSslMode" /> name, case ignored
///         (<c>None</c>, <c>Disabled</c>, <c>Preferred</c>, <c>Required</c>, <c>VerifyCA</c>,
///         <c>VerifyFull</c>). SQL Server: <c>Optional</c>, <c>Mandatory</c> or <c>Strict</c>, checked with
///         <see cref="SqlConnectionEncryptOption.TryParse" /> because
///         <see cref="SqlConnectionEncryptOption" /> is a class, not an enum, so
///         <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)" /> does not apply.
///     </para>
/// </summary>
public static class StorageConnectionStringAssembler
{
    /// <summary>
    ///     This method builds the connection string from the section of <paramref name="request" />
    ///     that matches <see cref="StorageEngineSwitchApiRequest.TargetEngine" />. For
    ///     <see cref="StorageEngineEnum.MongoDb" />, which has no section, it returns an empty
    ///     string; <see cref="IStorageEngineSwitchService.ScheduleSwitchAsync" /> then rejects the
    ///     target with its own message.
    /// </summary>
    /// <exception cref="ValidationException">
    ///     Thrown when the section matching <see cref="StorageEngineSwitchApiRequest.TargetEngine" />
    ///     is missing, or a required field is missing or out of range, or <c>encryptionMode</c> is
    ///     not a value that engine recognizes.
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
    ///     This method builds a PostgreSQL connection string with
    ///     <see cref="NpgsqlConnectionStringBuilder" />, checking <c>EncryptionMode</c> against
    ///     <see cref="Npgsql.SslMode" />.
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
    ///     This method builds a MySQL or MariaDB connection string with
    ///     <see cref="MySqlConnector.MySqlConnectionStringBuilder" />, checking
    ///     <c>EncryptionMode</c> against <see cref="MySqlConnector.MySqlSslMode" />.
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
    ///     This method builds a SQL Server connection string with
    ///     <see cref="SqlConnectionStringBuilder" />, checking <c>EncryptionMode</c> with
    ///     <see cref="SqlConnectionEncryptOption.TryParse" /> (<c>Optional</c>, <c>Mandatory</c> or
    ///     <c>Strict</c>), since that type is not an enum.
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
    ///     This method checks the fields the three server engines share (all but
    ///     <c>EncryptionMode</c>, which each engine checks itself) and throws a
    ///     <see cref="ValidationException" /> for the first problem. A missing section counts too:
    ///     naming <see cref="StorageEngineEnum.PostgreSql" /> without filling in
    ///     <see cref="StorageEngineSwitchApiRequest.PostgreSql" /> is as malformed as a blank field.
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
    ///     This record carries the fields of a <see cref="RelationalStorageConnectionFields" />
    ///     after <see cref="ValidateCommonFields" /> has checked all but
    ///     <see cref="EncryptionMode" />, with a <see langword="null" /> <see cref="Password" />
    ///     turned into an empty string so each builder method need not check it.
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
