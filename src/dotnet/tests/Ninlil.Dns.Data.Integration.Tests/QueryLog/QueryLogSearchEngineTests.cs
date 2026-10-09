using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.QueryLog;

/// <summary>
///     This class runs <see cref="QueryLogSearchTestsBase" /> on PostgreSQL (real container). It
///     needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlQueryLogSearchTests(PostgreSqlEngineFixture fixture)
    : QueryLogSearchTestsBase(fixture);

/// <summary>
///     This class runs <see cref="QueryLogSearchTestsBase" /> on MySQL (real container). It needs
///     Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlQueryLogSearchTests(MySqlEngineFixture fixture)
    : QueryLogSearchTestsBase(fixture);

/// <summary>
///     This class runs <see cref="QueryLogSearchTestsBase" /> on SQL Server (real container). It
///     needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerQueryLogSearchTests(SqlServerEngineFixture fixture)
    : QueryLogSearchTestsBase(fixture);

/// <summary>
///     This class runs <see cref="QueryLogSearchTestsBase" /> on a MongoDB replica set (real
///     container), as a regression guard: the MongoDB repository already escapes the term. It
///     needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbQueryLogSearchTests(MongoDbEngineFixture fixture)
    : QueryLogSearchTestsBase(fixture);

/// <summary>
///     This class runs <see cref="QueryLogSearchTestsBase" /> on a standalone MongoDB (real
///     container) to show the fixture works without a replica set. It needs Docker, so it
///     carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(MongoDbStandaloneCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbStandaloneQueryLogSearchTests(MongoDbStandaloneEngineFixture fixture)
    : QueryLogSearchTestsBase(fixture);
