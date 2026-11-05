using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Npgsql;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class recognizes a <see cref="DbUpdateException" /> that a unique index or a primary
///     key caused, on each of the four relational providers. <see cref="EntityFrameworkUnitOfWork" />
///     uses it to turn such a failure into the <c>ConflictException</c> that the API answers with
///     409. The provider driver exception sits in <see cref="Exception.InnerException" />; this
///     project already references every provider package, so the check matches the driver types
///     directly instead of going through a registered per-provider classifier. Any other failure
///     (a foreign key, a not-null column, a timeout) is not recognized and keeps its original
///     exception.
/// </summary>
internal static class UniqueConstraintViolation
{
    /// <summary>SQLite result code SQLITE_CONSTRAINT, the family of every constraint failure.</summary>
    private const int SqliteConstraint = 19;

    /// <summary>SQLite extended code SQLITE_CONSTRAINT_PRIMARYKEY.</summary>
    private const int SqliteConstraintPrimaryKey = 1555;

    /// <summary>SQLite extended code SQLITE_CONSTRAINT_UNIQUE.</summary>
    private const int SqliteConstraintUnique = 2067;

    /// <summary>PostgreSQL SQLSTATE unique_violation.</summary>
    private const string PostgreSqlUniqueViolation = "23505";

    /// <summary>MySQL error ER_DUP_ENTRY, raised for a unique index and for the primary key.</summary>
    private const int MySqlDuplicateEntry = 1062;

    /// <summary>SQL Server error 2601, a duplicate key in a unique index.</summary>
    private const int SqlServerDuplicateKeyIndex = 2601;

    /// <summary>SQL Server error 2627, a violation of a unique or primary key constraint.</summary>
    private const int SqlServerDuplicateKeyConstraint = 2627;

    /// <summary>
    ///     This method tells whether the save failed on a unique index or primary key.
    /// </summary>
    /// <param name="exception">The exception the save threw.</param>
    /// <param name="detail">
    ///     The text to log: the constraint name when the provider reports it (PostgreSQL),
    ///     otherwise the driver message, which names the index or the columns.
    /// </param>
    /// <returns><see langword="true" /> when a unique index or primary key rejected the write.</returns>
    public static bool IsUniqueViolation(DbUpdateException exception, out string detail)
    {
        detail = exception.InnerException?.Message;

        switch (exception.InnerException)
        {
            case SqliteException sqlite:
                return sqlite.SqliteErrorCode == SqliteConstraint
                    && sqlite.SqliteExtendedErrorCode
                        is SqliteConstraintUnique
                            or SqliteConstraintPrimaryKey;
            case PostgresException postgres:
                detail = postgres.ConstraintName ?? postgres.MessageText;
                return postgres.SqlState == PostgreSqlUniqueViolation;
            case MySqlException mySql:
                return mySql.Number == MySqlDuplicateEntry;
            case SqlException sqlServer:
                return sqlServer.Number
                    is SqlServerDuplicateKeyIndex
                        or SqlServerDuplicateKeyConstraint;
            default:
                return false;
        }
    }
}
