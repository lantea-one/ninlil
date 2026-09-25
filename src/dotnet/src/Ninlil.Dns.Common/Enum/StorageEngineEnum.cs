using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the database engines Ninlil can store its data in, chosen through
///     configuration. <see cref="Sqlite" /> is the default: it needs no database server and creates
///     its file automatically, so a fresh installation works at once. The four relational engines
///     share one Entity Framework Core model; <see cref="MongoDb" /> has a separate implementation
///     of the same repository contracts.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<StorageEngineEnum>>]
public enum StorageEngineEnum
{
    /// <summary>
    ///     SQLite, a serverless, single-file database. This is the default.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Sqlite")]
    Sqlite = 0,

    /// <summary>
    ///     PostgreSQL, an open-source, general-purpose relational database server.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "PostgreSql")]
    PostgreSql = 1,

    /// <summary>
    ///     MySQL or MariaDB, an open-source, general-purpose relational database server. Both
    ///     speak the same wire protocol and are served by the same driver.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "MySql")]
    MySql = 2,

    /// <summary>
    ///     Microsoft SQL Server, a commercial relational database server.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "SqlServer")]
    SqlServer = 3,

    /// <summary>
    ///     MongoDB, a document database server. It is not accessed through Entity Framework Core;
    ///     <c>Ninlil.Dns.Data.MongoDb</c> implements the repository contracts for it.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "MongoDb")]
    MongoDb = 4,
}
