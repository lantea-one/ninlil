// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Fixtures;

/// <summary>
///     This class runs one PostgreSQL 16 container for the PostgreSQL collection.
/// </summary>
public sealed class PostgreSqlEngineFixture() : EngineFixtureBase(new PostgreSqlEngineHost());
