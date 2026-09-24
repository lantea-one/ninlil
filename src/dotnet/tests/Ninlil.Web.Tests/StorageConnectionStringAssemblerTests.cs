using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Web.Models;
using Ninlil.Web.Services.Storage;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="StorageConnectionStringAssembler" /> at the unit level --
///     asserting the exact resulting ADO.NET connection string for representative structured-field
///     input per engine, and that a malformed request throws <see cref="ValidationException" />
///     rather than assembling a broken or misleading connection string. This is the real-instance,
///     no-network-server-required coverage this environment can offer
///     for PostgreSQL/MySQL/SQL Server -- no real instance of any of those three servers is
///     reachable here, so the SQLite assembly path is the one additionally exercised end to end
///     through a real live switch in <see cref="StorageControllerSwitchTests" />.
/// </summary>
public class StorageConnectionStringAssemblerTests
{
    [Fact]
    public void Assemble_ForSqlite_BuildsTheExpectedConnectionString()
    {
        string connectionString = StorageConnectionStringAssembler.Assemble(
            new StorageEngineSwitchApiRequest(
                StorageEngineEnum.Sqlite,
                Sqlite: new SqliteStorageConnectionFields("/var/lib/ninlil/ninlil.sqlite3"),
                PostgreSql: null,
                MySql: null,
                SqlServer: null
            )
        );

        Assert.Equal("Data Source=/var/lib/ninlil/ninlil.sqlite3", connectionString);
    }

    [Fact]
    public void Assemble_ForSqlite_WithNoFilePath_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            StorageConnectionStringAssembler.Assemble(
                new StorageEngineSwitchApiRequest(
                    StorageEngineEnum.Sqlite,
                    Sqlite: new SqliteStorageConnectionFields(""),
                    PostgreSql: null,
                    MySql: null,
                    SqlServer: null
                )
            )
        );
    }

    [Fact]
    public void Assemble_ForSqlite_WithNoSection_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            StorageConnectionStringAssembler.Assemble(
                new StorageEngineSwitchApiRequest(
                    StorageEngineEnum.Sqlite,
                    Sqlite: null,
                    PostgreSql: null,
                    MySql: null,
                    SqlServer: null
                )
            )
        );
    }

    [Fact]
    public void Assemble_ForPostgreSql_BuildsTheExpectedConnectionString()
    {
        string connectionString = StorageConnectionStringAssembler.Assemble(
            new StorageEngineSwitchApiRequest(
                StorageEngineEnum.PostgreSql,
                Sqlite: null,
                PostgreSql: new RelationalStorageConnectionFields(
                    "db.example.internal",
                    5432,
                    "ninlil",
                    "ninlil_app",
                    "correct-horse-battery-staple",
                    "Require"
                ),
                MySql: null,
                SqlServer: null
            )
        );

        Assert.Equal(
            "Host=db.example.internal;Port=5432;Database=ninlil;Username=ninlil_app;"
                + "Password=correct-horse-battery-staple;SSL Mode=Require",
            connectionString
        );
    }

    [Fact]
    public void Assemble_ForPostgreSql_WithAnInvalidEncryptionMode_ThrowsValidationException()
    {
        ValidationException exception = Assert.Throws<ValidationException>(() =>
            StorageConnectionStringAssembler.Assemble(
                new StorageEngineSwitchApiRequest(
                    StorageEngineEnum.PostgreSql,
                    Sqlite: null,
                    PostgreSql: new RelationalStorageConnectionFields(
                        "db.example.internal",
                        5432,
                        "ninlil",
                        "ninlil_app",
                        "correct-horse-battery-staple",
                        "NotARealMode"
                    ),
                    MySql: null,
                    SqlServer: null
                )
            )
        );

        Assert.Contains("PostgreSQL SSL mode", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Assemble_ForMySql_BuildsTheExpectedConnectionString()
    {
        string connectionString = StorageConnectionStringAssembler.Assemble(
            new StorageEngineSwitchApiRequest(
                StorageEngineEnum.MySql,
                Sqlite: null,
                PostgreSql: null,
                MySql: new RelationalStorageConnectionFields(
                    "db.example.internal",
                    3306,
                    "ninlil",
                    "ninlil_app",
                    "correct-horse-battery-staple",
                    "Required"
                ),
                SqlServer: null
            )
        );

        Assert.Equal(
            "Server=db.example.internal;Port=3306;User ID=ninlil_app;"
                + "Password=correct-horse-battery-staple;Database=ninlil;SSL Mode=Required",
            connectionString
        );
    }

    [Fact]
    public void Assemble_ForMySql_WithAnOutOfRangePort_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            StorageConnectionStringAssembler.Assemble(
                new StorageEngineSwitchApiRequest(
                    StorageEngineEnum.MySql,
                    Sqlite: null,
                    PostgreSql: null,
                    MySql: new RelationalStorageConnectionFields(
                        "db.example.internal",
                        0,
                        "ninlil",
                        "ninlil_app",
                        "correct-horse-battery-staple",
                        "Required"
                    ),
                    SqlServer: null
                )
            )
        );
    }

    [Fact]
    public void Assemble_ForSqlServer_BuildsTheExpectedConnectionString()
    {
        string connectionString = StorageConnectionStringAssembler.Assemble(
            new StorageEngineSwitchApiRequest(
                StorageEngineEnum.SqlServer,
                Sqlite: null,
                PostgreSql: null,
                MySql: null,
                SqlServer: new RelationalStorageConnectionFields(
                    "db.example.internal",
                    1433,
                    "ninlil",
                    "ninlil_app",
                    "correct-horse-battery-staple",
                    "Mandatory"
                )
            )
        );

        Assert.Equal(
            "Data Source=db.example.internal,1433;Initial Catalog=ninlil;"
                + "User ID=ninlil_app;Password=correct-horse-battery-staple;Encrypt=True",
            connectionString
        );
    }

    [Fact]
    public void Assemble_ForSqlServer_WithAnInvalidEncryptionMode_ThrowsValidationException()
    {
        ValidationException exception = Assert.Throws<ValidationException>(() =>
            StorageConnectionStringAssembler.Assemble(
                new StorageEngineSwitchApiRequest(
                    StorageEngineEnum.SqlServer,
                    Sqlite: null,
                    PostgreSql: null,
                    MySql: null,
                    SqlServer: new RelationalStorageConnectionFields(
                        "db.example.internal",
                        1433,
                        "ninlil",
                        "ninlil_app",
                        "correct-horse-battery-staple",
                        "NotARealMode"
                    )
                )
            )
        );

        Assert.Contains("SQL Server encryption mode", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Assemble_ForARelationalEngine_WithNoDatabaseName_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            StorageConnectionStringAssembler.Assemble(
                new StorageEngineSwitchApiRequest(
                    StorageEngineEnum.PostgreSql,
                    Sqlite: null,
                    PostgreSql: new RelationalStorageConnectionFields(
                        "db.example.internal",
                        5432,
                        "",
                        "ninlil_app",
                        "correct-horse-battery-staple",
                        "Require"
                    ),
                    MySql: null,
                    SqlServer: null
                )
            )
        );
    }

    [Fact]
    public void Assemble_ForMongoDb_ReturnsAnEmptyStringUnvalidated()
    {
        // MongoDB has no structured-field section here at all -- IStorageEngineSwitchService
        // itself already rejects this target with its own dedicated message, so this method
        // never needs to validate anything for it.
        string connectionString = StorageConnectionStringAssembler.Assemble(
            new StorageEngineSwitchApiRequest(
                StorageEngineEnum.MongoDb,
                Sqlite: null,
                PostgreSql: null,
                MySql: null,
                SqlServer: null
            )
        );

        Assert.Equal(string.Empty, connectionString);
    }
}
