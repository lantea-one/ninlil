// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Fixtures;

/// <summary>
///     This class provides the SQLite collection; it needs no container and no Docker.
/// </summary>
public sealed class SqliteEngineFixture() : EngineFixtureBase(new SqliteEngineHost());
