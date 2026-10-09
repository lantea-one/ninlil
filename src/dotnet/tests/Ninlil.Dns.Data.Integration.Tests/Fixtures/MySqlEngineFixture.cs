// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Fixtures;

/// <summary>
///     This class runs one MySQL 8.4 container for the MySQL collection. It connects as
///     <c>root</c> so that EF Core can create a database per test.
/// </summary>
public sealed class MySqlEngineFixture() : EngineFixtureBase(new MySqlEngineHost())
{
    /// <summary>
    ///     This method returns a connection string for the server's <c>mysql</c> system database
    ///     with pooling off, so every connection it opens is a new server connection (the
    ///     connection-count test relies on that).
    /// </summary>
    public string BuildNonPooledConnectionString() =>
        ((MySqlEngineHost)Host).BuildNonPooledConnectionString();
}
