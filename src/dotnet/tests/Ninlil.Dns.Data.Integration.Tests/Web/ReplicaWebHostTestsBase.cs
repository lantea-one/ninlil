using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Protocol.Dhcpv4;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Dns.Identity.Token;
using Ninlil.Dns.Server.Backup.Crypto;
using Ninlil.Dns.Server.Rewrites;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Web;

/// <summary>
///     This class holds the read-your-own-writes tests that start the real <c>Ninlil.Web</c>
///     application on a replicated database and drive it over HTTP with replication paused, so a
///     row written on the primary is missing on the replica. The tests prove, end to end through
///     authentication, the middleware, the controllers and the storage layer:
///     <list type="bullet">
///         <item>a user with no recent write reads a classified endpoint from the replica;</item>
///         <item>
///             the same user right after a save reads the primary for the sticky window, with a JWT
///             or an API token, and reads the replica again once the window has passed;
///         </item>
///         <item>
///             sign-in, DHCP allocation and backup export read the primary although the replica is
///             behind;
///         </item>
///         <item>
///             the DNS rewrite cache sees a rewrite saved through the API at once although the
///             replica is behind (its reload after a local change reads the primary);
///         </item>
///         <item>
///             <c>GET /api/storage</c> and <c>GET /status</c> report the replica, its lag and the
///             routed reads, and turn Degraded while the replica serves no reads.
///         </item>
///     </list>
///     One subclass per engine supplies the host and the engine settings. The sticky window is
///     <see cref="StickyWindowSeconds" /> seconds and the lag limit one second; the replica health
///     check runs once per hour (the tests run it themselves), so a paused replica stays eligible
///     and the only thing that moves reads to the primary is the sticky rule under test.
/// </summary>
/// <param name="host">The replicated host the subclass's fixture started.</param>
public abstract class ReplicaWebHostTestsBase(ReplicatedEngineHost host)
{
    /// <summary>
    ///     This constant contains the sticky window the host runs with, in seconds.
    /// </summary>
    protected const int StickyWindowSeconds = 4;

    private const string UserPassword = "Replica-User-1234!";

    /// <summary>
    ///     This gate lets one web host exist at a time: the host reads its settings from
    ///     process-wide environment variables.
    /// </summary>
    private static readonly SemaphoreSlim HostGate = new(1, 1);

    /// <summary>
    ///     This method creates the database with the Ninlil schema on the primary and waits until
    ///     the replica has it.
    /// </summary>
    /// <returns>The database name.</returns>
    protected abstract Task<string> PrepareDatabaseAsync();

    /// <summary>
    ///     This method adds the engine and split settings, as <c>NINLIL_*</c> variables.
    /// </summary>
    /// <param name="variables">The variables being built.</param>
    /// <param name="databaseName">The database the host is for.</param>
    protected abstract void ConfigureStorage(
        Dictionary<string, string> variables,
        string databaseName
    );

    /// <summary>
    ///     This class is a running host plus the clients and helpers the tests share.
    /// </summary>
    protected sealed class RunningHost(
        ReplicatedWebApplicationFactory factory,
        ReplicatedEngineHost host,
        string database
    ) : IAsyncDisposable
    {
        private readonly List<IDisposable> disposables = [];

        public string Database => database;

        public IServiceProvider Services => factory.Services;

        public IStorageReadRouter Router =>
            factory.Services.GetRequiredService<IStorageReadRouter>();

        public HttpClient NewClient()
        {
            HttpClient client = factory.CreateClient();
            disposables.Add(client);
            return client;
        }

        public async Task<HttpClient> LoginAsync(string userName, string password)
        {
            HttpClient client = NewClient();
            HttpResponseMessage login = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest(userName, password)
            );
            login.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                (await login.Content.ReadFromJsonAsync<LoginResponse>()).AccessToken
            );
            return client;
        }

        public Task<HttpClient> LoginAdminAsync() =>
            LoginAsync(
                ReplicatedWebApplicationFactory.AdminUserName,
                ReplicatedWebApplicationFactory.AdminPassword
            );

        /// <summary>
        ///     This method creates a user with the given permissions, as the administrator does
        ///     over the API, and returns its identifier.
        /// </summary>
        public async Task<Guid> CreateUserAsync(
            HttpClient admin,
            string userName,
            params string[] permissions
        )
        {
            HttpResponseMessage created = await admin.PostAsJsonAsync(
                "/api/users",
                new CreateUserRequest(
                    userName,
                    UserPassword,
                    $"{userName}@localhost.local",
                    userName
                )
            );
            created.EnsureSuccessStatusCode();
            Guid id = (await created.Content.ReadFromJsonAsync<UserSummary>()).Id;
            foreach (string permission in permissions)
                (
                    await admin.PostAsJsonAsync(
                        $"/api/users/{id}/permissions",
                        new UserPermissionRequest(permission)
                    )
                ).EnsureSuccessStatusCode();
            return id;
        }

        public Task<HttpClient> LoginUserAsync(string userName) =>
            LoginAsync(userName, UserPassword);

        /// <summary>
        ///     This method waits for the replica, then runs the replica health check until the
        ///     replica is eligible, as it is in a host whose monitor has run. MongoDB optimes have
        ///     a resolution of one second, so right after a catch-up a healthy secondary can
        ///     still read as a second or two behind; the check is repeated for a few seconds.
        /// </summary>
        public async Task SettleAsync()
        {
            await host.WaitForCatchUpAsync(database);
            ReplicaHealthMonitor monitor =
                factory.Services.GetRequiredService<ReplicaHealthMonitor>();
            DateTime giveUp = DateTime.UtcNow.AddSeconds(20);
            ReplicaHealthStatus health = await monitor.CheckOnceAsync(CancellationToken.None);
            while (!health.IsEligible && DateTime.UtcNow < giveUp)
            {
                await Task.Delay(500);
                health = await monitor.CheckOnceAsync(CancellationToken.None);
            }

            Assert.True(health.IsEligible, health.Reason);
        }

        public async Task<string[]> RewriteDomainsAsync(HttpClient client)
        {
            PagedResult<DnsRewriteDetail> page = await client.GetFromJsonAsync<
                PagedResult<DnsRewriteDetail>
            >("/api/dns-rewrites?take=100");
            return [.. page.Items.Select(item => item.Domain)];
        }

        public async Task CreateRewriteAsync(HttpClient client, string domain)
        {
            HttpResponseMessage created = await client.PostAsJsonAsync(
                "/api/dns-rewrites",
                new DnsRewriteWriteRequest(domain, "192.0.2.7")
            );
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (IDisposable disposable in disposables)
                disposable.Dispose();
            await factory.DisposeAsync();
        }
    }

    /// <summary>
    ///     This method runs <paramref name="test" /> against a freshly started host on a database
    ///     of its own, with replication running when the test starts and resumed when it ends.
    /// </summary>
    private async Task WithHostAsync(Func<RunningHost, Task> test)
    {
        await HostGate.WaitAsync();
        try
        {
            string database = await PrepareDatabaseAsync();
            Dictionary<string, string> variables = new()
            {
                ["NINLIL_STORAGE_READ_REPLICA_STICKY_AFTER_WRITE_SECONDS"] =
                    StickyWindowSeconds.ToString(),
                ["NINLIL_STORAGE_READ_REPLICA_MAX_LAG_SECONDS"] = "1",
                ["NINLIL_STORAGE_READ_REPLICA_LAG_CHECK_INTERVAL_SECONDS"] = "3600",
            };
            ConfigureStorage(variables, database);

            await using RunningHost running = new(
                new ReplicatedWebApplicationFactory(variables),
                host,
                database
            );
            try
            {
                await running.SettleAsync();
                await test(running);
            }
            finally
            {
                await host.ResumeReplicationAsync(database);
            }
        }
        finally
        {
            HostGate.Release();
        }
    }

    /// <summary>
    ///     With replication paused, a user who saved a rewrite lists it at once (the primary),
    ///     another user who saved nothing does not see it (the replica, which the router counts),
    ///     the writer reads the replica again once the window has passed, and everybody sees the
    ///     rewrite after the replica catches up.
    /// </summary>
    [Fact]
    public Task ClassifiedEndpoint_WhileReplicationIsPaused_SendsTheWriterToThePrimaryOnlyWithinTheWindow() =>
        WithHostAsync(async web =>
        {
            HttpClient admin = await web.LoginAdminAsync();
            string suffix = Guid.NewGuid().ToString("N")[..8];
            await web.CreateUserAsync(
                admin,
                $"writer{suffix}",
                "dnsrewrites:read",
                "dnsrewrites:write"
            );
            await web.CreateUserAsync(admin, $"reader{suffix}", "dnsrewrites:read");
            HttpClient writer = await web.LoginUserAsync($"writer{suffix}");
            HttpClient reader = await web.LoginUserAsync($"reader{suffix}");
            await web.SettleAsync();

            string domain = $"ryow-{suffix}.example.";
            await host.PauseReplicationAsync(web.Database);

            await web.CreateRewriteAsync(writer, domain);

            // The writer's own list comes from the primary.
            long replicaReadsBefore = web.Router.ReadsToReplica;
            Assert.Contains(domain, await web.RewriteDomainsAsync(writer));
            Assert.Equal(replicaReadsBefore, web.Router.ReadsToReplica);

            // A user with no recent write reads the replica, which does not have the row yet.
            Assert.DoesNotContain(domain, await web.RewriteDomainsAsync(reader));
            Assert.Equal(replicaReadsBefore + 1, web.Router.ReadsToReplica);

            // After the window the writer reads the replica too (still behind).
            await Task.Delay(TimeSpan.FromSeconds(StickyWindowSeconds + 1));
            Assert.DoesNotContain(domain, await web.RewriteDomainsAsync(writer));
            Assert.Equal(replicaReadsBefore + 2, web.Router.ReadsToReplica);

            // Once the replica has caught up everybody sees the row.
            await host.ResumeReplicationAsync(web.Database);
            await host.WaitForCatchUpAsync(web.Database);
            Assert.Contains(domain, await web.RewriteDomainsAsync(reader));
            Assert.Contains(domain, await web.RewriteDomainsAsync(writer));
        });

    /// <summary>
    ///     A static API token acts as its owner: a save through the JWT is seen by a list through
    ///     the token (the same user key), and another user's token still reads the replica.
    /// </summary>
    [Fact]
    public Task ApiTokenOfTheWriter_WhileReplicationIsPaused_SeesTheWritersOwnSave() =>
        WithHostAsync(async web =>
        {
            HttpClient admin = await web.LoginAdminAsync();
            string suffix = Guid.NewGuid().ToString("N")[..8];
            Guid writerId = await web.CreateUserAsync(
                admin,
                $"tokwriter{suffix}",
                "dnsrewrites:read",
                "dnsrewrites:write"
            );
            Guid readerId = await web.CreateUserAsync(
                admin,
                $"tokreader{suffix}",
                "dnsrewrites:read"
            );
            HttpClient writer = await web.LoginUserAsync($"tokwriter{suffix}");

            string writerToken;
            string readerToken;
            await using (AsyncServiceScope scope = web.Services.CreateAsyncScope())
            {
                IApiTokenService tokens =
                    scope.ServiceProvider.GetRequiredService<IApiTokenService>();
                writerToken = (await tokens.CreateStaticTokenAsync(writerId, "w", null)).RawToken;
                readerToken = (await tokens.CreateStaticTokenAsync(readerId, "r", null)).RawToken;
            }

            await web.SettleAsync();
            string domain = $"token-{suffix}.example.";
            await host.PauseReplicationAsync(web.Database);

            await web.CreateRewriteAsync(writer, domain);

            HttpClient viaWriterToken = web.NewClient();
            viaWriterToken.DefaultRequestHeaders.TryAddWithoutValidation(
                "Authorization",
                $"ApiToken {writerToken}"
            );
            HttpClient viaReaderToken = web.NewClient();
            viaReaderToken.DefaultRequestHeaders.TryAddWithoutValidation(
                "Authorization",
                $"ApiToken {readerToken}"
            );

            Assert.Contains(domain, await web.RewriteDomainsAsync(viaWriterToken));
            Assert.DoesNotContain(domain, await web.RewriteDomainsAsync(viaReaderToken));
        });

    /// <summary>
    ///     With replication paused and the replica eligible, sign-in, DHCP allocation and backup
    ///     export still see rows that exist only on the primary: a user created a moment ago signs
    ///     in, the DHCP engine offers an address from a scope created a moment ago, and a backup
    ///     taken by a user who saved nothing contains a rewrite created a moment ago. An
    ///     attributed list read by the same user at the same moment does not see them, so the
    ///     replica really was behind.
    /// </summary>
    [Fact]
    public Task SignInDhcpAllocationAndBackup_WhileReplicationIsPaused_ReadThePrimary() =>
        WithHostAsync(async web =>
        {
            HttpClient admin = await web.LoginAdminAsync();
            string suffix = Guid.NewGuid().ToString("N")[..8];
            await web.CreateUserAsync(
                admin,
                $"auditor{suffix}",
                "users:read",
                "dnsrewrites:read",
                "backup:read"
            );
            HttpClient auditor = await web.LoginUserAsync($"auditor{suffix}");
            await web.SettleAsync();
            await host.PauseReplicationAsync(web.Database);

            // Rows that exist only on the primary.
            string newUser = $"fresh{suffix}";
            await web.CreateUserAsync(admin, newUser);
            string domain = $"backup-{suffix}.example.";
            await web.CreateRewriteAsync(admin, domain);
            string scopeName = $"scope-{suffix}";
            HttpResponseMessage scopeCreated = await admin.PostAsJsonAsync(
                "/api/dhcp-scopes",
                new
                {
                    name = scopeName,
                    protocolVersion = "IPv4",
                    rangeStartAddress = "198.51.100.100",
                    rangeEndAddress = "198.51.100.110",
                    subnetPrefixLength = 24,
                    defaultLeaseDuration = "1.00:00:00",
                    maxLeaseDuration = "7.00:00:00",
                }
            );
            Assert.Equal(HttpStatusCode.Created, scopeCreated.StatusCode);

            // The replica is behind: a replica read does not see the rewrite or the user.
            Assert.DoesNotContain(domain, await web.RewriteDomainsAsync(auditor));
            PagedResult<UserSummary> users = await auditor.GetFromJsonAsync<
                PagedResult<UserSummary>
            >($"/api/users?search={newUser}");
            Assert.DoesNotContain(users.Items, user => user.UserName == newUser);

            // Sign-in finds the new user.
            (await web.LoginUserAsync(newUser)).Dispose();

            // The DHCP engine finds the new scope and offers an address from it.
            IDhcpv4RequestHandler handler =
                web.Services.GetRequiredService<IDhcpv4RequestHandler>();
            Dhcpv4Message discover = new()
            {
                OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
                TransactionId = 0x5eed,
                HardwareAddressLength = 6,
                ClientHardwareAddress = [0xaa, 0xbb, 0xcc, 0x00, 0x00, 0x01],
            };
            discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
            Dhcpv4Message offer = await handler.HandleRequestAsync(
                discover,
                new IPEndPoint(IPAddress.Loopback, 68),
                CancellationToken.None
            );
            Assert.NotNull(offer);
            Assert.StartsWith("198.51.100.", offer.YourIpAddress.ToString());

            // The backup contains the new rewrite.
            HttpResponseMessage backup = await auditor.PostAsJsonAsync(
                "/api/backup",
                new BackupCreateRequest(null)
            );
            Assert.Equal(HttpStatusCode.OK, backup.StatusCode);
            Assert.Contains(domain, ReadBackupText(await backup.Content.ReadAsByteArrayAsync()));
        });

    /// <summary>
    ///     With replication paused and the replica eligible, the DNS rewrite cache (the real
    ///     singleton of the running application) answers with a rewrite saved through the API on
    ///     its very next read, while the replica still lacks the row. The save expires the cache,
    ///     and an expired load reads the primary.
    /// </summary>
    [Fact]
    public Task RewriteCache_RightAfterAnApiSaveWhileReplicationIsPaused_AnswersFromThePrimary() =>
        WithHostAsync(async web =>
        {
            HttpClient admin = await web.LoginAdminAsync();
            string suffix = Guid.NewGuid().ToString("N")[..8];
            DnsRewriteCache cache = web.Services.GetRequiredService<DnsRewriteCache>();
            await cache.GetAsync();
            await web.SettleAsync();
            await host.PauseReplicationAsync(web.Database);

            string domain = $"dnspath-{suffix}.example";
            await web.CreateRewriteAsync(admin, domain);

            // A replica read does not have the row, the cache does.
            await using (AsyncServiceScope scope = web.Services.CreateAsyncScope())
            {
                IServiceScopeFactory factory =
                    scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();
                await using AsyncServiceScope replica = factory.CreateReplicaReadScope();
                Assert.DoesNotContain(
                    await replica
                        .ServiceProvider.GetRequiredService<IDnsRewriteRepository>()
                        .ListEnabledAsync(),
                    rewrite => rewrite.Domain.TrimEnd('.') == domain
                );
            }

            Assert.True((await cache.GetAsync()).Exact.ContainsKey(domain));
        });

    /// <summary>
    ///     The storage status lists the primary and the replica, counts routed reads, and follows
    ///     the replica when it falls behind: while replication is paused and writes continue, the
    ///     health monitor's check makes the replica ineligible, <c>GET /api/storage</c> says why
    ///     and <c>GET /status</c> shows the storage component Degraded with the reason; after
    ///     the replica catches up both return to normal.
    /// </summary>
    [Fact]
    public Task StorageStatus_FollowsTheReplicaIntoLagAndBack() =>
        WithHostAsync(async web =>
        {
            HttpClient admin = await web.LoginAdminAsync();
            string suffix = Guid.NewGuid().ToString("N")[..8];
            await web.CreateUserAsync(admin, $"statusreader{suffix}", "dnsrewrites:read");
            HttpClient reader = await web.LoginUserAsync($"statusreader{suffix}");
            await web.SettleAsync();

            // A replica read, then the status.
            await web.RewriteDomainsAsync(reader);
            StorageStatus healthy = await admin.GetFromJsonAsync<StorageStatus>("/api/storage");
            Assert.NotNull(healthy.ReadReplica);
            Assert.True(healthy.ReadReplica.Configured);
            Assert.True(healthy.ReadReplica.Eligible, healthy.ReadReplica.Reason);
            Assert.True(healthy.ReadReplica.SchemaCurrent);
            Assert.NotNull(healthy.ReadReplica.LastCheckedAtUtc);
            Assert.True(healthy.ReadReplica.ReadsToReplica >= 1);
            Assert.True(healthy.ReadReplica.ReadsToPrimary >= 1);
            Assert.NotEmpty(healthy.Primary.Hosts);
            Assert.DoesNotContain("Password", JsonSerializer.Serialize(healthy));
            Assert.Equal("Healthy", await StorageComponentStatusAsync(admin));

            // The replica falls behind: writes continue on the primary.
            await host.PauseReplicationAsync(web.Database);
            ReplicaHealthMonitor monitor = web.Services.GetRequiredService<ReplicaHealthMonitor>();
            DateTime giveUp = DateTime.UtcNow.AddSeconds(60);
            int index = 0;
            while (web.Router.ReplicaHealth.IsEligible && DateTime.UtcNow < giveUp)
            {
                await web.CreateRewriteAsync(admin, $"lag{index++}-{suffix}.example.");
                await Task.Delay(1500);
                await monitor.CheckOnceAsync(CancellationToken.None);
            }

            StorageStatus lagging = await admin.GetFromJsonAsync<StorageStatus>("/api/storage");
            Assert.False(lagging.ReadReplica.Eligible);
            Assert.NotEmpty(lagging.ReadReplica.Reason);
            Assert.Equal("Degraded", await StorageComponentStatusAsync(admin));

            // Back to normal after the catch-up.
            await host.ResumeReplicationAsync(web.Database);
            await web.SettleAsync();
            StorageStatus recovered = await admin.GetFromJsonAsync<StorageStatus>("/api/storage");
            Assert.True(recovered.ReadReplica.Eligible, recovered.ReadReplica.Reason);
            Assert.Equal("Healthy", await StorageComponentStatusAsync(admin));
        });

    private static async Task<string> StorageComponentStatusAsync(HttpClient admin)
    {
        StatusResponse status = await admin.GetFromJsonAsync<StatusResponse>("/status");
        return status.Components.Single(component => component.Name == "storage").Status;
    }

    private static string ReadBackupText(byte[] archive)
    {
        using MemoryStream zip = new(BackupArchiveCodec.Unwrap(archive));
        using ZipArchive entries = new(zip, ZipArchiveMode.Read);
        StringBuilder text = new();
        foreach (ZipArchiveEntry entry in entries.Entries)
        {
            using StreamReader reader = new(entry.Open(), Encoding.UTF8);
            text.AppendLine(reader.ReadToEnd());
        }

        return text.ToString();
    }
}
