using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the database engines Ninlil can store its data in, selected by
///     the operator through configuration. <see cref="Sqlite" /> is the default: it requires no
///     separate database server and creates its own file automatically, so a fresh installation
///     works immediately. The four relational options are all served by Entity Framework Core
///     against the same provider-agnostic model; <see cref="MongoDb" /> is served by its own,
///     independent implementation of the same repository contracts.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<StorageEngineEnum>>]
public enum StorageEngineEnum
{
    /// <summary>
    ///     SQLite, a self-contained, serverless, single-file database. This is the default engine
    ///     used when no other engine has been configured.
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
    ///     MongoDB, a document-oriented database server. Unlike the four relational engines
    ///     above, MongoDB is not accessed through Entity Framework Core; it has its own,
    ///     purpose-built implementation of Ninlil's repository contracts.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "MongoDb")]
    MongoDb = 4,
}
