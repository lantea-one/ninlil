// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Fixtures;

/// <summary>
///     This class runs one SQL Server 2022 container for the SQL Server collection.
/// </summary>
public sealed class SqlServerEngineFixture() : EngineFixtureBase(new SqlServerEngineHost());
