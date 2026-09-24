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
///     This class covers <see cref="ClusterChannelController" />'s own TSIG authentication path
///     end to end -- the peer-to-peer half of the cluster channel, distinct from
///     <see cref="ClusterController" />'s own JWT-authenticated, human-operator surface. Every
///     request here is built and signed by hand, exactly the way
///     <c>ClusterPeerHttpClientFactory.SendSignedAsync</c> itself would, rather than going
///     through that class directly, so these tests exercise the real wire format
///     <see cref="ClusterTsigAuthenticationHandler" /> actually parses.
///     <para>
///         <c>JoinComplete</c>'s own success path deliberately has no test here: it requires
///         <c>ClusterOptions.Enable</c> to be live-true, and this suite's own shared
///         <see cref="NinlilDnsWebApplicationFactory" /> has a real, already-documented limitation
///         (see <c>RecursiveOptionsHotReloadTests</c>' own doc comment) where its isolated
///         runtime-settings file is never registered as its own reload-on-change
///         <see cref="Microsoft.Extensions.Configuration.IConfiguration" />
///         source -- a settings write here reaches the file correctly, but the already-running
///         host's own live options never observe it. That success path is proven instead by a
///         real, separate two-process smoke test, which starts each node fresh (reading
///         its own configuration at real startup, not live-reload) and therefore has no such
///         limitation.
///     </para>
/// </summary>
[Collection(WebTestCollection.Name)]
public class ClusterChannelControllerTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     This method seeds a real, persisted TSIG key -- the local state that exists on a real
    ///     node the moment it has either created an invite or already joined a cluster.
    /// </summary>
    private async Task<(string KeyName, byte[] Secret)> SeedTsigKeyAsync()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ITsigKeyRepository tsigKeyRepository =
            scope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        TsigSecretProtector secretProtector =
            factory.Services.GetRequiredService<TsigSecretProtector>();

        byte[] secret = "a-shared-cluster-channel-secret!"u8.ToArray();
        string keyName = $"cluster-{Guid.NewGuid():N}";

        await tsigKeyRepository.AddAsync(
            new TsigKeyEntity
            {
                Id = Guid.NewGuid(),
                Name = keyName,
                AlgorithmName = "hmac-sha256",
                SecretEncrypted = secretProtector.Protect(secret),
                IsDisabled = false,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        return (keyName, secret);
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
        (string keyName, byte[] secret) = await SeedTsigKeyAsync();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(
            BuildSignedRequest(
                "/api/cluster/channel/heartbeat",
                keyName,
                secret,
                new ClusterHeartbeatRequest(Guid.NewGuid(), DateTimeOffset.UtcNow)
            )
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

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
        (string keyName, byte[] _) = await SeedTsigKeyAsync();
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
    ///     This test proves the cluster config-sync channel's own inbound dispatch end to end, over the real HTTP +
    ///     TSIG authentication pipeline, not merely against <see cref="FilterListClusterConfigSyncApplier" />
    ///     directly (that class's own create/update/delete/last-write-wins logic is already
    ///     covered exhaustively in <c>Ninlil.Dns.Server.ClusterSync.Tests</c>) -- what this test
    ///     alone confirms is that <see cref="ClusterChannelController.ConfigSync" /> actually
    ///     resolves and invokes the right registered <see cref="IClusterConfigSyncApplier" /> for
    ///     a real <c>"FilterList"</c> envelope, ending in a real, persisted row a fresh repository
    ///     read can see.
    /// </summary>
    [Fact]
    public async Task ConfigSync_WithAValidSignatureAndARecognizedEntityType_PersistsTheChange()
    {
        (string keyName, byte[] secret) = await SeedTsigKeyAsync();
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
    ///     An envelope naming an entity type no registered applier recognizes must still answer
    ///     <c>200 OK</c> -- see <see cref="ClusterChannelController.ConfigSync" />'s own doc
    ///     comment for why (a newer peer must never look like a channel failure to an older one).
    /// </summary>
    [Fact]
    public async Task ConfigSync_WithAnUnrecognizedEntityType_StillReturnsOk()
    {
        (string keyName, byte[] secret) = await SeedTsigKeyAsync();
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
    ///     This test proves the cluster config-sync channel's own inbound dispatch for a <c>"User"</c> envelope, over
    ///     the real HTTP + TSIG authentication pipeline -- <see cref="UserClusterConfigSyncApplier" />'s
    ///     own create/update/delete/collision logic is already covered exhaustively in
    ///     <c>Ninlil.Dns.Server.ClusterSync.Tests</c>; what this test alone confirms is that
    ///     <see cref="ClusterChannelController.ConfigSync" /> actually resolves and invokes the
    ///     right registered applier for a real <c>"User"</c> entity type, ending in a real,
    ///     persisted row a fresh repository read can see.
    /// </summary>
    [Fact]
    public async Task ConfigSync_WithAUserEntityTypeAndAValidSignature_PersistsTheAccount()
    {
        (string keyName, byte[] secret) = await SeedTsigKeyAsync();
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
    ///     This test proves the cluster's own best-effort cluster-wide username-uniqueness check
    ///     endpoint, over the real HTTP + TSIG authentication pipeline: a username that already
    ///     exists locally is reported as existing.
    /// </summary>
    [Fact]
    public async Task UsernameCheck_ForAnExistingAccount_ReportsExists()
    {
        (string keyName, byte[] secret) = await SeedTsigKeyAsync();
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
        (string keyName, byte[] secret) = await SeedTsigKeyAsync();
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
    ///     This test proves the cluster's own aggregate-query-stats channel endpoint over the real
    ///     HTTP + TSIG authentication pipeline: a validly signed request always receives a real
    ///     snapshot back, with every count zero-or-greater (this suite's own shared database may
    ///     already carry query-log rows from another test).
    /// </summary>
    [Fact]
    public async Task QueryStats_WithAValidSignature_ReturnsARealSnapshot()
    {
        (string keyName, byte[] secret) = await SeedTsigKeyAsync();
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
