using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Authentication;
using Ninlil.Dns.Server.Cluster.Channel;
using Ninlil.Dns.Server.ClusterSync.Filtering;
using Ninlil.Dns.Server.ClusterSync.Identity;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Tsig;
using Ninlil.Web.Controllers;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the TSIG-authenticated peer endpoints of
///     <see cref="ClusterChannelController" /> over HTTP on the shared in-process host, as opposed
///     to the operator endpoints of <see cref="ClusterController" />, which use a JWT. Each test
///     saves a TSIG key directly through the repository, usually linked to a cluster node as
///     after a join, and signs its request by hand, as
///     <c>ClusterPeerHttpClientFactory.SendSignedAsync</c> would, so the tests exercise the header
///     format and key checks of <see cref="ClusterTsigAuthenticationHandler" />. They cover
///     heartbeat (including certificate re-pinning), the join callback with invite keys,
///     config-sync for filter lists, users and unknown entity types, username checks and query
///     statistics, each with a valid signature and without one, and check that a key that is not
///     a cluster key (an operator's zone-transfer key) is refused everywhere. The join callback
///     needs clustering on, which <see cref="LiveClusterOptionsScope" /> provides.
/// </summary>
[Collection(WebTestCollection.Name)]
public class ClusterChannelControllerTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     This method saves an enabled TSIG key, optionally linked to a cluster node or marked as
    ///     an unused invite key. With neither, it is an operator's ordinary key, such as one made
    ///     for zone transfers.
    /// </summary>
    private async Task<(string KeyName, byte[] Secret)> SeedTsigKeyAsync(
        Guid? clusterNodeId = null,
        DateTimeOffset? clusterInviteExpiresAtUtc = null,
        string keyName = null
    )
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ITsigKeyRepository tsigKeyRepository =
            scope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        TsigSecretProtector secretProtector =
            factory.Services.GetRequiredService<TsigSecretProtector>();

        byte[] secret = "a-shared-cluster-channel-secret!"u8.ToArray();
        keyName ??= $"cluster-{Guid.NewGuid():N}";

        await tsigKeyRepository.AddAsync(
            new TsigKeyEntity
            {
                Id = Guid.NewGuid(),
                Name = keyName,
                AlgorithmName = "hmac-sha256",
                SecretEncrypted = secretProtector.Protect(secret),
                IsDisabled = false,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ClusterNodeId = clusterNodeId,
                ClusterInviteExpiresAtUtc = clusterInviteExpiresAtUtc,
            }
        );
        await unitOfWork.SaveChangesAsync();

        return (keyName, secret);
    }

    /// <summary>
    ///     This method saves a peer and its linked key, the state a node has after a join.
    /// </summary>
    private async Task<(string KeyName, byte[] Secret, Guid RemoteNodeId)> SeedPeerAsync(
        string thumbprint = null
    )
    {
        Guid nodeRowId = Guid.NewGuid();
        Guid remoteNodeId = Guid.NewGuid();
        (string keyName, byte[] secret) = await SeedTsigKeyAsync(clusterNodeId: nodeRowId);

        using IServiceScope scope = factory.Services.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<IClusterNodeRepository>()
            .AddAsync(
                new ClusterNodeEntity
                {
                    Id = nodeRowId,
                    Name = "seeded-peer",
                    RemoteNodeId = remoteNodeId,
                    Host = "192.0.2.10",
                    Port = 443,
                    TsigKeyName = keyName,
                    TlsCertificateThumbprint = thumbprint ?? new string('a', 64),
                    Status = ClusterNodeStatusEnum.Joining,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        return (keyName, secret, remoteNodeId);
    }

    private static HttpRequestMessage BuildSignedRequest(
        string path,
        string keyName,
        byte[] secret,
        object body
    )
    {
        string jsonBody = JsonSerializer.Serialize(body);
        byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        byte[] mac = ClusterTsigRequestSigner.ComputeMac(
            secret,
            "POST",
            path,
            timestamp,
            bodyBytes
        );

        HttpRequestMessage request = new(HttpMethod.Post, path)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(
            ClusterTsigAuthenticationDefaults.SignatureHeaderName,
            ClusterTsigRequestSigner.FormatHeaderValue(keyName, timestamp, mac)
        );

        return request;
    }

    [Fact]
    public async Task Heartbeat_WithAValidSignature_ReturnsOk()
    {
        (string keyName, byte[] secret, Guid remoteNodeId) = await SeedPeerAsync();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(
            BuildSignedRequest(
                "/api/cluster/channel/heartbeat",
                keyName,
                secret,
                new ClusterHeartbeatRequest(remoteNodeId, DateTimeOffset.UtcNow)
            )
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    ///     This test checks that a peer announcing a renewed certificate in its signed heartbeat
    ///     gets the new thumbprint pinned, so this node's probes of it keep working.
    /// </summary>
    [Fact]
    public async Task Heartbeat_AnnouncingANewCertificate_RepinsThePeer()
    {
        (string keyName, byte[] secret, Guid remoteNodeId) = await SeedPeerAsync(
            thumbprint: new string('a', 64)
        );
        string renewedThumbprint = new('c', 64);
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(
            BuildSignedRequest(
                "/api/cluster/channel/heartbeat",
                keyName,
                secret,
                new ClusterHeartbeatRequest(remoteNodeId, DateTimeOffset.UtcNow, renewedThumbprint)
            )
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using IServiceScope scope = factory.Services.CreateScope();
        ClusterNodeEntity peer = await scope
            .ServiceProvider.GetRequiredService<IClusterNodeRepository>()
            .FindByRemoteNodeIdAsync(remoteNodeId);
        Assert.Equal(renewedThumbprint, peer.TlsCertificateThumbprint);
    }

    /// <summary>
    ///     This test checks that a heartbeat naming another node than the one its key belongs to
    ///     is refused, so one peer cannot re-pin another peer's certificate.
    /// </summary>
    [Fact]
    public async Task Heartbeat_NamingADifferentSender_ReturnsBadRequestAndKeepsThePin()
    {
        (string _, byte[] _, Guid otherRemoteNodeId) = await SeedPeerAsync();
        (string keyName, byte[] secret, Guid _) = await SeedPeerAsync();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(
            BuildSignedRequest(
                "/api/cluster/channel/heartbeat",
                keyName,
                secret,
                new ClusterHeartbeatRequest(otherRemoteNodeId, DateTimeOffset.UtcNow, new('d', 64))
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using IServiceScope scope = factory.Services.CreateScope();
        ClusterNodeEntity other = await scope
            .ServiceProvider.GetRequiredService<IClusterNodeRepository>()
            .FindByRemoteNodeIdAsync(otherRemoteNodeId);
        Assert.Equal(new string('a', 64), other.TlsCertificateThumbprint);
    }

    /// <summary>
    ///     This test checks that an operator's key that belongs to no peer, such as one made for
    ///     zone transfers, is refused on every cluster-channel endpoint even with a valid
    ///     signature, the join callback included.
    /// </summary>
    [Theory]
    [InlineData("/api/cluster/channel/heartbeat")]
    [InlineData("/api/cluster/channel/join-complete")]
    [InlineData("/api/cluster/channel/config-sync")]
    [InlineData("/api/cluster/channel/username-check")]
    [InlineData("/api/cluster/channel/query-stats")]
    public async Task AnyEndpoint_SignedWithAZoneTransferKey_ReturnsUnauthorized(string path)
    {
        (string keyName, byte[] secret) = await SeedTsigKeyAsync();
        HttpClient client = factory.CreateClient();
        using LiveClusterOptionsScope cluster = new(factory, advertisedHost: "ninlil-a.example");

        HttpResponseMessage response = await client.SendAsync(
            BuildSignedRequest(path, keyName, secret, BodyFor(path))
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    ///     This test checks that an unused invite key only signs the join callback: it gets 401
    ///     everywhere else, config sync above all.
    /// </summary>
    [Theory]
    [InlineData("/api/cluster/channel/heartbeat")]
    [InlineData("/api/cluster/channel/config-sync")]
    [InlineData("/api/cluster/channel/username-check")]
    [InlineData("/api/cluster/channel/query-stats")]
    public async Task AnyEndpointButJoinComplete_SignedWithAnInviteKey_ReturnsUnauthorized(
        string path
    )
    {
        (string keyName, byte[] secret) = await SeedTsigKeyAsync(
            clusterInviteExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(10)
        );
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(
            BuildSignedRequest(path, keyName, secret, BodyFor(path))
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    ///     This test checks the inviting node's side of a join: an unused invite key registers the
    ///     joiner, is linked to it, and cannot register a second node afterwards.
    /// </summary>
    [Fact]
    public async Task JoinComplete_WithAnUnusedInviteKey_RegistersThePeerAndUsesUpTheInvite()
    {
        (string keyName, byte[] secret) = await SeedTsigKeyAsync(
            clusterInviteExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(10)
        );
        HttpClient client = factory.CreateClient();
        using LiveClusterOptionsScope cluster = new(factory, advertisedHost: "ninlil-a.example");
        Guid joinerNodeId = Guid.NewGuid();

        HttpResponseMessage response = await client.SendAsync(
            BuildSignedRequest(
                "/api/cluster/channel/join-complete",
                keyName,
                secret,
                new ClusterJoinCompleteRequest(joinerNodeId, "192.0.2.20", 443, new('b', 64), null)
            )
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ClusterNodeEntity node = await scope
                .ServiceProvider.GetRequiredService<IClusterNodeRepository>()
                .FindByRemoteNodeIdAsync(joinerNodeId);
            TsigKeyEntity key = await scope
                .ServiceProvider.GetRequiredService<ITsigKeyRepository>()
                .FindByNameAsync(keyName);
            Assert.NotNull(node);
            Assert.Equal(node.Id, key.ClusterNodeId);
            Assert.Null(key.ClusterInviteExpiresAtUtc);
        }

        // The same invite cannot admit another node.
        HttpResponseMessage secondResponse = await client.SendAsync(
            BuildSignedRequest(
                "/api/cluster/channel/join-complete",
                keyName,
                secret,
                new ClusterJoinCompleteRequest(
                    Guid.NewGuid(),
                    "192.0.2.21",
                    443,
                    new('b', 64),
                    null
                )
            )
        );
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    /// <summary>
    ///     This test checks that the inviting node itself refuses an expired invite, whatever the
    ///     joining node believes about the expiry.
    /// </summary>
    [Fact]
    public async Task JoinComplete_WithAnExpiredInviteKey_ReturnsUnauthorized()
    {
        (string keyName, byte[] secret) = await SeedTsigKeyAsync(
            clusterInviteExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1)
        );
        HttpClient client = factory.CreateClient();
        using LiveClusterOptionsScope cluster = new(factory, advertisedHost: "ninlil-a.example");
        Guid joinerNodeId = Guid.NewGuid();

        HttpResponseMessage response = await client.SendAsync(
            BuildSignedRequest(
                "/api/cluster/channel/join-complete",
                keyName,
                secret,
                new ClusterJoinCompleteRequest(joinerNodeId, "192.0.2.22", 443, new('b', 64), null)
            )
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using IServiceScope scope = factory.Services.CreateScope();
        Assert.Null(
            await scope
                .ServiceProvider.GetRequiredService<IClusterNodeRepository>()
                .FindByRemoteNodeIdAsync(joinerNodeId)
        );
    }

    /// <summary>
    ///     This method builds a well-formed body for one of the channel endpoints, so a refusal
    ///     comes from authentication rather than model binding.
    /// </summary>
    private static object BodyFor(string path) =>
        path switch
        {
            "/api/cluster/channel/heartbeat" => new ClusterHeartbeatRequest(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow
            ),
            "/api/cluster/channel/join-complete" => new ClusterJoinCompleteRequest(
                Guid.NewGuid(),
                "192.0.2.30",
                443,
                new string('b', 64),
                null
            ),
            "/api/cluster/channel/config-sync" => new ClusterConfigSyncEnvelope(
                FilterListClusterConfigSyncApplier.EntityTypeName,
                Guid.NewGuid().ToString(),
                IsDeleted: true,
                null,
                DateTimeOffset.UtcNow,
                Guid.NewGuid()
            ),
            "/api/cluster/channel/username-check" => new ClusterUsernameCheckRequest("ANYONE"),
            _ => new ClusterQueryStatsRequest(Guid.NewGuid()),
        };

    [Fact]
    public async Task Heartbeat_WithNoSignatureHeaderAtAll_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/cluster/channel/heartbeat",
            new ClusterHeartbeatRequest(Guid.NewGuid(), DateTimeOffset.UtcNow)
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_SignedWithTheWrongSecret_ReturnsUnauthorized()
    {
        (string keyName, byte[] _, Guid _) = await SeedPeerAsync();
        byte[] wrongSecret = "a-completely-different-secret!!"u8.ToArray();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(
            BuildSignedRequest(
                "/api/cluster/channel/heartbeat",
                keyName,
                wrongSecret,
                new ClusterHeartbeatRequest(Guid.NewGuid(), DateTimeOffset.UtcNow)
            )
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_SignedWithAnUnknownKeyName_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(
            BuildSignedRequest(
                "/api/cluster/channel/heartbeat",
                "cluster-a-key-that-was-never-registered",
                "some-secret-bytes-32-characters"u8.ToArray(),
                new ClusterHeartbeatRequest(Guid.NewGuid(), DateTimeOffset.UtcNow)
            )
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task JoinComplete_WithNoSignatureHeaderAtAll_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/cluster/channel/join-complete",
            new ClusterJoinCompleteRequest(
                Guid.NewGuid(),
                "192.168.1.77",
                443,
                new string('b', 64),
                null
            )
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    ///     This test checks that <see cref="ClusterChannelController.ConfigSync" />, behind TSIG
    ///     authentication, hands a <c>"FilterList"</c> envelope to the matching
    ///     <see cref="IClusterConfigSyncApplier" /> and that the list is then saved. The applier's
    ///     create, update, delete and last-write-wins logic is covered in
    ///     <c>Ninlil.Dns.Server.ClusterSync.Tests</c>.
    /// </summary>
    [Fact]
    public async Task ConfigSync_WithAValidSignatureAndARecognizedEntityType_PersistsTheChange()
    {
        (string keyName, byte[] secret, Guid _) = await SeedPeerAsync();
        HttpClient client = factory.CreateClient();

        Guid listId = Guid.NewGuid();
        string payload = JsonSerializer.Serialize(
            new FilterListSyncPayload(
                "Synced-From-Peer",
                DnsFilterListKindEnum.Domain,
                DnsFilterListActionEnum.Block,
                DnsFilterListSourceEnum.Local,
                null,
                null,
                false,
                null
            )
        );

        HttpResponseMessage response = await client.SendAsync(
            BuildSignedRequest(
                "/api/cluster/channel/config-sync",
                keyName,
                secret,
                new ClusterConfigSyncEnvelope(
                    FilterListClusterConfigSyncApplier.EntityTypeName,
                    listId.ToString(),
                    IsDeleted: false,
                    payload,
                    DateTimeOffset.UtcNow,
                    Guid.NewGuid()
                )
            )
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using IServiceScope scope = factory.Services.CreateScope();
        IDnsFilterListRepository filterListRepository =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListRepository>();
        DnsFilterListEntity list = await filterListRepository.GetByIdAsync(listId);

        Assert.NotNull(list);
        Assert.Equal("Synced-From-Peer", list.Name);
    }

    /// <summary>
    ///     An envelope naming an entity type no applier recognizes must still get <c>200 OK</c>,
    ///     so a newer peer never looks like a channel failure to an older one (see
    ///     <see cref="ClusterChannelController.ConfigSync" />).
    /// </summary>
    [Fact]
    public async Task ConfigSync_WithAnUnrecognizedEntityType_StillReturnsOk()
    {
        (string keyName, byte[] secret, Guid _) = await SeedPeerAsync();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(
            BuildSignedRequest(
                "/api/cluster/channel/config-sync",
                keyName,
                secret,
                new ClusterConfigSyncEnvelope(
                    "SomeFutureEntityTypeThisNodeDoesNotKnowAboutYet",
                    Guid.NewGuid().ToString(),
                    IsDeleted: false,
                    "{}",
                    DateTimeOffset.UtcNow,
                    Guid.NewGuid()
                )
            )
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ConfigSync_WithNoSignatureHeaderAtAll_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/cluster/channel/config-sync",
            new ClusterConfigSyncEnvelope(
                FilterListClusterConfigSyncApplier.EntityTypeName,
                Guid.NewGuid().ToString(),
                IsDeleted: true,
                null,
                DateTimeOffset.UtcNow,
                Guid.NewGuid()
            )
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    ///     This test checks that a <c>"User"</c> envelope reaches
    ///     <see cref="UserClusterConfigSyncApplier" /> and the account is then saved. The applier
    ///     itself is covered in <c>Ninlil.Dns.Server.ClusterSync.Tests</c>.
    /// </summary>
    [Fact]
    public async Task ConfigSync_WithAUserEntityTypeAndAValidSignature_PersistsTheAccount()
    {
        (string keyName, byte[] secret, Guid _) = await SeedPeerAsync();
        HttpClient client = factory.CreateClient();

        Guid userId = Guid.NewGuid();
        string payload = JsonSerializer.Serialize(
            new UserSyncPayload(
                "synced-from-peer",
                "SYNCED-FROM-PEER",
                "Synced From Peer",
                null,
                null,
                false,
                "a-fake-password-hash",
                Guid.NewGuid().ToString("N"),
                Guid.NewGuid().ToString("N"),
                null,
                false,
                false,
                null,
                null,
                null,
                true,
                0,
                false,
                null,
                null,
                null,
                null,
                [],
                []
            )
        );

        HttpResponseMessage response = await client.SendAsync(
            BuildSignedRequest(
                "/api/cluster/channel/config-sync",
                keyName,
                secret,
                new ClusterConfigSyncEnvelope(
                    UserClusterConfigSyncApplier.EntityTypeName,
                    userId.ToString(),
                    IsDeleted: false,
                    payload,
                    DateTimeOffset.UtcNow,
                    Guid.NewGuid()
                )
            )
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using IServiceScope scope = factory.Services.CreateScope();
        IUserRepository userRepository =
            scope.ServiceProvider.GetRequiredService<IUserRepository>();
        UserEntity user = await userRepository.GetByIdAsync(userId);

        Assert.NotNull(user);
        Assert.Equal("synced-from-peer", user.UserName);
    }

    /// <summary>
    ///     This test checks the username check that peers use for best-effort cluster-wide
    ///     uniqueness: a user name that exists locally is reported as existing.
    /// </summary>
    [Fact]
    public async Task UsernameCheck_ForAnExistingAccount_ReportsExists()
    {
        (string keyName, byte[] secret, Guid _) = await SeedPeerAsync();
        HttpClient client = factory.CreateClient();

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IUserRepository userRepository =
                scope.ServiceProvider.GetRequiredService<IUserRepository>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await userRepository.AddAsync(
                new UserEntity
                {
                    Id = Guid.NewGuid(),
                    UserName = "already-here",
                    NormalizedUserName = "ALREADY-HERE",
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
            await unitOfWork.SaveChangesAsync();
        }

        HttpResponseMessage response = await client.SendAsync(
            BuildSignedRequest(
                "/api/cluster/channel/username-check",
                keyName,
                secret,
                new ClusterUsernameCheckRequest("ALREADY-HERE")
            )
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ClusterUsernameCheckResult result =
            await response.Content.ReadFromJsonAsync<ClusterUsernameCheckResult>();
        Assert.True(result.Exists);
    }

    [Fact]
    public async Task UsernameCheck_ForAUsernameThatDoesNotExist_ReportsNotExists()
    {
        (string keyName, byte[] secret, Guid _) = await SeedPeerAsync();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(
            BuildSignedRequest(
                "/api/cluster/channel/username-check",
                keyName,
                secret,
                new ClusterUsernameCheckRequest($"NOBODY-{Guid.NewGuid():N}")
            )
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ClusterUsernameCheckResult result =
            await response.Content.ReadFromJsonAsync<ClusterUsernameCheckResult>();
        Assert.False(result.Exists);
    }

    [Fact]
    public async Task UsernameCheck_WithNoSignatureHeaderAtAll_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/cluster/channel/username-check",
            new ClusterUsernameCheckRequest("ANYONE")
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    ///     This test checks the query statistics endpoint: a signed request gets a snapshot back.
    ///     The counts are only checked to be zero or more, since other tests may have written
    ///     query log rows to the shared database.
    /// </summary>
    [Fact]
    public async Task QueryStats_WithAValidSignature_ReturnsARealSnapshot()
    {
        (string keyName, byte[] secret, Guid _) = await SeedPeerAsync();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(
            BuildSignedRequest(
                "/api/cluster/channel/query-stats",
                keyName,
                secret,
                new ClusterQueryStatsRequest(Guid.NewGuid())
            )
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ClusterQueryStatsSnapshot snapshot =
            await response.Content.ReadFromJsonAsync<ClusterQueryStatsSnapshot>();

        Assert.NotNull(snapshot);
        Assert.True(snapshot.QueriesLastHour >= 0);
        Assert.True(snapshot.QueriesLast24Hours >= 0);
        Assert.True(snapshot.TotalRetainedQueries >= 0);
    }

    [Fact]
    public async Task QueryStats_WithNoSignatureHeaderAtAll_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/cluster/channel/query-stats",
            new ClusterQueryStatsRequest(Guid.NewGuid())
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
