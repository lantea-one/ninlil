// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Fixtures;

/// <summary>
///     This class runs one MongoDB 8 container as a single-node replica set for the MongoDB
///     collection. Transactions need a replica set. The standalone variant, for code paths that
///     must work without one, is <see cref="MongoDbStandaloneEngineFixture" />.
/// </summary>
public sealed class MongoDbEngineFixture() : EngineFixtureBase(new MongoDbEngineHost());

/// <summary>
///     This class runs one standalone MongoDB 8 container (no replica set) for the standalone
///     MongoDB collection, with the same pinned image as <see cref="MongoDbEngineFixture" />.
/// </summary>
public sealed class MongoDbStandaloneEngineFixture()
    : EngineFixtureBase(new MongoDbEngineHost(replicaSet: false));
