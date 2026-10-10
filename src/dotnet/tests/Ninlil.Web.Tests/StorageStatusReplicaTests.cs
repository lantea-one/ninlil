using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Ninlil.Web.Services.Storage;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the replica and host parts of <c>GET /api/storage</c> without a replica
///     server. The HTTP test runs on the shared in-process host, which has no read/write split, so
///     the answer must carry <c>readReplica: null</c> and no connection detail. The
///     <see cref="StorageHostLister" /> tests feed it connection strings that hold passwords and
///     check that only hosts and ports come out. The replicated case (a live replica, lag and
///     counters) is covered in <c>Ninlil.Dns.Data.Integration.Tests</c>.
/// </summary>
[Collection(WebTestCollection.Name)]
public class StorageStatusReplicaTests(NinlilDnsWebApplicationFactory factory)
{
    [Fact]
    public async Task Status_WithoutASplit_HasNoReadReplicaAndNoSecrets()
    {
        HttpClient client = factory.CreateClient();
        HttpResponseMessage loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(
                NinlilDnsWebApplicationFactory.AdminUserName,
                NinlilDnsWebApplicationFactory.AdminPassword
            )
        );
        loginResponse.EnsureSuccessStatusCode();
        LoginResponse login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            login.AccessToken
        );

        HttpResponseMessage response = await client.GetAsync("/api/storage");
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync()
        );
        JsonElement root = document.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("readReplica").ValueKind);
        Assert.Equal(
            JsonValueKind.Array,
            root.GetProperty("primary").GetProperty("hosts").ValueKind
        );
        Assert.DoesNotContain("Password", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void List_PostgreSqlWithSeveralHosts_GivesHostAndPortForEach()
    {
        IReadOnlyList<string> hosts = StorageHostLister.List(
            StorageEngineEnum.PostgreSql,
            "Host=a.example,b.example:6432;Port=5433;Database=ninlil;Username=u;Password=secret"
        );

        Assert.Equal(["a.example:5433", "b.example:6432"], hosts);
    }

    [Fact]
    public void List_MySqlWithSeveralHosts_GivesHostAndPortForEach()
    {
        IReadOnlyList<string> hosts = StorageHostLister.List(
            StorageEngineEnum.MySql,
            "Server=m1.example,m2.example;Database=ninlil;User=u;Password=secret"
        );

        Assert.Equal(["m1.example:3306", "m2.example:3306"], hosts);
    }

    [Fact]
    public void List_SqlServerListener_GivesTheDataSourceWithoutPrefixOrSecrets()
    {
        IReadOnlyList<string> hosts = StorageHostLister.List(
            StorageEngineEnum.SqlServer,
            "Server=tcp:listener.example,1433;Database=ninlil;User Id=u;Password=secret"
        );

        Assert.Equal(["listener.example,1433"], hosts);
    }

    [Fact]
    public void List_MongoDbReplicaSetUrl_GivesEveryMember()
    {
        IReadOnlyList<string> hosts = StorageHostLister.List(
            StorageEngineEnum.MongoDb,
            "mongodb://u:secret@m1.example:27017,m2.example/ninlil?replicaSet=rs0"
        );

        Assert.Equal(["m1.example:27017", "m2.example:27017"], hosts);
    }

    [Fact]
    public void List_SqliteOrUnparsableOrEmpty_GivesNoHosts()
    {
        Assert.Empty(StorageHostLister.List(StorageEngineEnum.Sqlite, "Data Source=/x/y.db"));
        Assert.Empty(
            StorageHostLister.List(StorageEngineEnum.PostgreSql, "not a=connection=string;;=")
        );
        Assert.Empty(StorageHostLister.List(StorageEngineEnum.MongoDb, "http://nonsense"));
        Assert.Empty(StorageHostLister.List(StorageEngineEnum.MySql, ""));
        Assert.Empty(StorageHostLister.List(StorageEngineEnum.MySql, null));
    }
}
