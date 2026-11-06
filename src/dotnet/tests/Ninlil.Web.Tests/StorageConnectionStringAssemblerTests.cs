using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Web.Models;
using Ninlil.Web.Services.Storage;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class tests <see cref="StorageConnectionStringAssembler" /> directly, without a host or
///     a database server. For each relational engine it checks the ADO.NET connection string built
///     from sample structured fields, and that a malformed request throws
///     <see cref="ValidationException" /> instead of producing a broken connection string. No
///     PostgreSQL, MySQL or SQL Server instance is available to the tests, so only the SQLite path
///     is also exercised by a live switch, in <see cref="StorageControllerSwitchTests" />.
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
        // MongoDB has no structured-field section; IStorageEngineSwitchService rejects that
        // target with its own message, so this method does not validate it.
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

    private static StorageEngineSwitchApiRequest RelationalRequest(
        StorageEngineEnum engine,
        string encryptionMode
    )
    {
        RelationalStorageConnectionFields fields = new(
            "db.example.internal",
            engine == StorageEngineEnum.MySql ? 3306 : 5432,
            "ninlil",
            "ninlil_app",
            "correct-horse-battery-staple",
            encryptionMode
        );
        return new StorageEngineSwitchApiRequest(
            engine,
            Sqlite: null,
            PostgreSql: engine == StorageEngineEnum.PostgreSql ? fields : null,
            MySql: engine == StorageEngineEnum.MySql ? fields : null,
            SqlServer: null
        );
    }

    [Theory]
    [InlineData(StorageEngineEnum.PostgreSql, "1")]
    [InlineData(StorageEngineEnum.PostgreSql, "99")]
    [InlineData(StorageEngineEnum.PostgreSql, "Require,Prefer")]
    [InlineData(StorageEngineEnum.MySql, "1")]
    [InlineData(StorageEngineEnum.MySql, "99")]
    [InlineData(StorageEngineEnum.MySql, "None,Required")]
    public void Assemble_WithADigitStringEncryptionMode_ThrowsValidationException(
        StorageEngineEnum engine,
        string mode
    ) =>
        Assert.Throws<ValidationException>(() =>
            StorageConnectionStringAssembler.Assemble(RelationalRequest(engine, mode))
        );

    [Theory]
    [InlineData(StorageEngineEnum.PostgreSql, "require")]
    [InlineData(StorageEngineEnum.PostgreSql, "VERIFYFULL")]
    [InlineData(StorageEngineEnum.MySql, "required")]
    [InlineData(StorageEngineEnum.MySql, " VerifyCA ")]
    public void Assemble_WithAnEncryptionModeNameInAnyCase_BuildsTheConnectionString(
        StorageEngineEnum engine,
        string mode
    ) =>
        Assert.False(
            string.IsNullOrEmpty(
                StorageConnectionStringAssembler.Assemble(RelationalRequest(engine, mode))
            )
        );
}
