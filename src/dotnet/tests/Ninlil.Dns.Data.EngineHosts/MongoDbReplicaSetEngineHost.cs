using System.Net;
using System.Net.Sockets;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MongoDB.Bson;
using MongoDB.Driver;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.EngineHosts;

/// <summary>
///     This class runs a three-member MongoDB 8 replica set (<c>rs0</c>), one container per
///     member, with member 0 as the preferred primary (priority 2). The members share the first
///     container's network namespace, and each listens on its own free port that is published on
///     the same number on the host. The set names its members <c>address:port</c>, where the
///     address is the one the test process reaches published container ports on: loopback when
///     Docker runs on the same host (the containers share one network namespace, so loopback
///     also reaches every member from inside), or the address of the host's gateway when the test
///     process itself runs in a container (<c>TESTCONTAINERS_HOST_OVERRIDE</c>, set by
///     <c>in-container.sh --docker</c>). A numeric address needs no name resolution inside the
///     member containers. An ordinary replica-set client with a read preference therefore works
///     from the test process. The set runs without authentication. Apply on a secondary is paused with the
///     <c>pauseBatchApplicationBeforeCompletion</c> fail point (the server runs with <c>enableTestCommands</c>).
/// </summary>
public sealed class MongoDbReplicaSetEngineHost : ReplicatedEngineHost
{
    private const string ReplicaSetName = "rs0";

    private readonly int[] _ports = [FreePort(), FreePort(), FreePort()];
    private readonly List<IContainer> _members = [];
    private string _address = "localhost";

    /// <inheritdoc />
    public override StorageEngineEnum Engine => StorageEngineEnum.MongoDb;

    /// <summary>
    ///     This property contains the host ports of the three members, primary-preferred first.
    /// </summary>
    public IReadOnlyList<int> Ports => _ports;

    /// <inheritdoc />
    public override async Task StartAsync()
    {
        IContainer first = CreateMember(0, null);
        _members.Add(first);
        await first.StartAsync();

        // The Docker endpoint's host name is loopback on a plain host and the override host
        // inside a toolchain container. The set advertises its numeric address because the
        // members must reach each other on it and cannot resolve the override name.
        _address = await ResolveAddressAsync(first.Hostname);
        for (int index = 1; index < _ports.Length; index++)
        {
            IContainer member = CreateMember(index, first.Id);
            _members.Add(member);
            await member.StartAsync();
        }

        await using MongoClientHolder holder = new(DirectUrl(_address, _ports[0]));
        BsonArray members = [];
        for (int index = 0; index < _ports.Length; index++)
        {
            members.Add(
                new BsonDocument
                {
                    { "_id", index },
                    { "host", $"{_address}:{_ports[index]}" },
                    { "priority", index == 0 ? 2 : 1 },
                }
            );
        }

        await holder
            .Client.GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(
                new BsonDocument
                {
                    {
                        "replSetInitiate",
                        new BsonDocument { { "_id", ReplicaSetName }, { "members", members } }
                    },
                }
            );

        await PollAsync("member 0 to become primary", () => IsPrimaryAsync(_address, _ports[0]));
        await PollAsync(
            "both secondaries to be in the set",
            async () => await CountStateAsync(_address, _ports[0], "SECONDARY") == 2
        );
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        // The first container owns the network namespace, so it goes last.
        for (int index = _members.Count - 1; index >= 0; index--)
            await _members[index].DisposeAsync();
    }

    /// <summary>
    ///     This method returns the replica-set URL with the primary read preference, for
    ///     <paramref name="databaseName" />. The three members are listed as
    ///     <c>address:port</c>, which is also how they name themselves.
    /// </summary>
    public override string BuildConnectionString(string databaseName) =>
        $"mongodb://{Members}/{databaseName}?replicaSet={ReplicaSetName}";

    /// <summary>
    ///     This method returns the replica-set URL with the <c>secondary</c> read preference, so
    ///     reads go to a secondary and a write still goes to the primary.
    /// </summary>
    public override string BuildReplicaConnectionString(string databaseName) =>
        $"{BuildConnectionString(databaseName)}&readPreference=secondary";

    /// <summary>
    ///     This method returns a direct-connection URL to the member with the given index (0 is
    ///     the preferred primary), so a test can write to a secondary and see the refusal.
    /// </summary>
    public string BuildDirectConnectionString(int memberIndex) =>
        DirectUrl(_address, _ports[memberIndex]);

    /// <summary>
    ///     This method stops oplog apply on both secondaries, so writes on the primary stay
    ///     invisible to secondary reads until <see cref="ResumeReplicationAsync" />.
    /// </summary>
    public override async Task PauseReplicationAsync(string databaseName = null)
    {
        foreach (int port in _ports.Skip(1))
            await SetFailPointAsync(
                _address,
                port,
                "pauseBatchApplicationBeforeCompletion",
                "alwaysOn"
            );
    }

    /// <inheritdoc />
    public override async Task ResumeReplicationAsync(string databaseName = null)
    {
        foreach (int port in _ports.Skip(1))
            await SetFailPointAsync(_address, port, "pauseBatchApplicationBeforeCompletion", "off");
    }

    /// <inheritdoc />
    public override async Task WaitForCatchUpAsync(
        string databaseName = null,
        TimeSpan? timeout = null
    )
    {
        // A majority-acknowledged no-op write on the primary returns once a majority of the
        // set holds it; two of three members is the primary and one secondary, so the other
        // secondary is checked explicitly through its last applied optime.
        BsonTimestamp target = await LastAppliedAsync(_address, _ports[0]);
        foreach (int port in _ports.Skip(1))
        {
            await PollAsync(
                $"the secondary on port {port} to apply the oplog",
                async () => (await LastAppliedAsync(_address, port)) >= target,
                timeout
            );
        }
    }

    private string Members => string.Join(",", _ports.Select(port => $"{_address}:{port}"));

    private IContainer CreateMember(int index, string networkOwnerId)
    {
        int port = _ports[index];
        ContainerBuilder builder = new ContainerBuilder(MongoDbEngineHost.Image)
            .WithCommand(
                "mongod",
                "--replSet",
                ReplicaSetName,
                "--port",
                port.ToString(),
                "--bind_ip_all",
                "--setParameter",
                "enableTestCommands=1"
            )
            .WithCreateParameterModifier(parameters =>
            {
                // Same open-file limit as MongoDbEngineHost: the server aborts on 1024.
                parameters.HostConfig.Ulimits =
                [
                    new Ulimit
                    {
                        Name = "nofile",
                        Soft = 64000,
                        Hard = 64000,
                    },
                ];
                if (networkOwnerId != null)
                    parameters.HostConfig.NetworkMode = $"container:{networkOwnerId}";
            })
            .WithWaitStrategy(
                Wait.ForUnixContainer().UntilMessageIsLogged("Waiting for connections")
            );

        // Only the first container owns a network namespace, so it publishes every member's port.
        if (networkOwnerId == null)
        {
            foreach (int published in _ports)
                builder = builder.WithPortBinding(published, published);
        }

        return builder.Build();
    }

    private static string DirectUrl(string address, int port) =>
        $"mongodb://{address}:{port}/?directConnection=true";

    private static async Task<string> ResolveAddressAsync(string hostName)
    {
        if (IPAddress.TryParse(hostName, out _))
            return hostName;
        IPAddress[] addresses = await System.Net.Dns.GetHostAddressesAsync(hostName);
        IPAddress v4 = addresses.First(address =>
            address.AddressFamily == AddressFamily.InterNetwork
        );
        return v4.ToString();
    }

    private static async Task<bool> IsPrimaryAsync(string address, int port)
    {
        await using MongoClientHolder holder = new(DirectUrl(address, port));
        BsonDocument hello = await holder
            .Client.GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1));
        return hello.GetValue("isWritablePrimary", false).AsBoolean;
    }

    private static async Task<int> CountStateAsync(string address, int port, string stateName)
    {
        await using MongoClientHolder holder = new(DirectUrl(address, port));
        BsonDocument status = await holder
            .Client.GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(new BsonDocument("replSetGetStatus", 1));
        return status["members"].AsBsonArray.Count(member => member["stateStr"] == stateName);
    }

    private static async Task<BsonTimestamp> LastAppliedAsync(string address, int port)
    {
        await using MongoClientHolder holder = new(DirectUrl(address, port));
        BsonDocument status = await holder
            .Client.GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(new BsonDocument("replSetGetStatus", 1));
        return status["optimes"]["appliedOpTime"]["ts"].AsBsonTimestamp;
    }

    private static async Task SetFailPointAsync(string address, int port, string name, string mode)
    {
        await using MongoClientHolder holder = new(DirectUrl(address, port));
        await holder
            .Client.GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(
                new BsonDocument { { "configureFailPoint", name }, { "mode", mode } }
            );
    }

    private static int FreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>
    ///     This type owns a short-lived direct-connection client and closes it afterwards.
    /// </summary>
    private sealed class MongoClientHolder(string url) : IAsyncDisposable
    {
        public MongoClient Client { get; } = new(url);

        public ValueTask DisposeAsync()
        {
            Client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
