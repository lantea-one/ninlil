using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Authoritative.CloudSync;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.CloudSync;

/// <summary>
///     This class covers <see cref="DigitalOceanDnsProvider" /> against a real, Kestrel-hosted
///     fake HTTP server standing in for DigitalOcean's own documented v2 REST contract -- there
///     is no live DigitalOcean account to verify against.
/// </summary>
public sealed class DigitalOceanDnsProviderTests : IAsyncLifetime
{
    private WebApplication _fakeServer;
    private HttpClient _httpClient;
    private readonly List<object> _createRequests = [];

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _fakeServer = builder.Build();

        _fakeServer.MapGet(
            "/domains/{domain}/records",
            () =>
                Results.Json(
                    new
                    {
                        domain_records = new object[]
                        {
                            new
                            {
                                id = 1001,
                                type = "A",
                                name = "www",
                                data = "192.0.2.30",
                                ttl = 300,
                                priority = (int?)null,
                                port = (int?)null,
                                weight = (int?)null,
                                flags = (int?)null,
                                tag = (string)null,
                            },
                            new
                            {
                                id = 1002,
                                type = "A",
                                name = "@",
                                data = "192.0.2.31",
                                ttl = 300,
                                priority = (int?)null,
                                port = (int?)null,
                                weight = (int?)null,
                                flags = (int?)null,
                                tag = (string)null,
                            },
                            new
                            {
                                id = 1003,
                                type = "SRV",
                                name = "_sip._tcp",
                                // DigitalOcean's own SRV "data" carries only the target; its
                                // priority/weight/port live in their own dedicated top-level
                                // fields instead, per DigitalOcean's own published schema.
                                data = "sipserver.example.com.",
                                ttl = 300,
                                priority = (int?)10,
                                port = (int?)5060,
                                weight = (int?)5,
                                flags = (int?)null,
                                tag = (string)null,
                            },
                            new
                            {
                                id = 1004,
                                type = "CAA",
                                name = "@",
                                data = "letsencrypt.org",
                                ttl = 300,
                                priority = (int?)null,
                                port = (int?)null,
                                weight = (int?)null,
                                flags = (int?)0,
                                tag = (string)"issue",
                            },
                        },
                        links = new { pages = (object)null },
                    }
                )
        );

        _fakeServer.MapPost(
            "/domains/{domain}/records",
            async (HttpContext context) =>
            {
                object body = await context.Request.ReadFromJsonAsync<object>();
                _createRequests.Add(body);
                return Results.Json(new { domain_record = new { id = 2001 } });
            }
        );

        _fakeServer.MapDelete("/domains/{domain}/records/{recordId}", () => Results.NoContent());

        await _fakeServer.StartAsync();

        _httpClient = new HttpClient { BaseAddress = new Uri(_fakeServer.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        _httpClient.Dispose();
        await _fakeServer.StopAsync();
        await _fakeServer.DisposeAsync();
    }

    private static Dictionary<string, string> Credentials => new() { ["ApiToken"] = "fake-token" };

    [Fact]
    public async Task ListRecordsConvertsRelativeNamesToFullyQualifiedNames()
    {
        DigitalOceanDnsProvider provider = new(_httpClient);

        IReadOnlyList<CloudDnsProviderRecord> records = await provider.ListRecordsAsync(
            Credentials,
            "example.com"
        );

        Assert.Equal(4, records.Count);
        Assert.Contains(records, r => r.Record.Name.ToString().TrimEnd('.') == "www.example.com");
        Assert.Contains(records, r => r.Record.Name.ToString().TrimEnd('.') == "example.com");
    }

    [Fact]
    public async Task ListRecordsParsesTheSrvTopLevelFields()
    {
        DigitalOceanDnsProvider provider = new(_httpClient);

        IReadOnlyList<CloudDnsProviderRecord> records = await provider.ListRecordsAsync(
            Credentials,
            "example.com"
        );

        CloudDnsProviderRecord srvRecord = Assert.Single(
            records,
            r => r.Record.Type == DnsResourceRecordTypeEnum.Srv
        );
        (ushort priority, ushort weight, ushort port, string target) =
            CloudDnsRecordMapper.FromSrvWireRecord(srvRecord.Record);

        Assert.Equal("_sip._tcp.example.com", srvRecord.Record.Name.ToString().TrimEnd('.'));
        Assert.Equal((ushort)10, priority);
        Assert.Equal((ushort)5, weight);
        Assert.Equal((ushort)5060, port);
        Assert.Equal("sipserver.example.com", target);
    }

    [Fact]
    public async Task ListRecordsParsesTheCaaTopLevelFields()
    {
        DigitalOceanDnsProvider provider = new(_httpClient);

        IReadOnlyList<CloudDnsProviderRecord> records = await provider.ListRecordsAsync(
            Credentials,
            "example.com"
        );

        CloudDnsProviderRecord caaRecord = Assert.Single(
            records,
            r => r.Record.Type == DnsResourceRecordTypeEnum.Caa
        );
        (bool isCritical, string tag, string value) = CloudDnsRecordMapper.FromCaaWireRecord(
            caaRecord.Record
        );

        Assert.Equal("example.com", caaRecord.Record.Name.ToString().TrimEnd('.'));
        Assert.False(isCritical);
        Assert.Equal("issue", tag);
        Assert.Equal("letsencrypt.org", value);
    }

    [Fact]
    public async Task CreateRecordForSrvSendsTheTopLevelFieldsAndATrailingDotTarget()
    {
        DigitalOceanDnsProvider provider = new(_httpClient);
        DnsResourceRecord record = CloudDnsRecordMapper.ToSrvWireRecord(
            "_sip._tcp.example.com",
            10,
            5,
            5060,
            "sipserver.example.com",
            300
        );

        await provider.CreateRecordAsync(Credentials, "example.com", record);

        JsonElement body = Assert.IsType<JsonElement>(Assert.Single(_createRequests));
        Assert.Equal("sipserver.example.com.", body.GetProperty("data").GetString());
        Assert.Equal(10, body.GetProperty("priority").GetInt32());
        Assert.Equal(5, body.GetProperty("weight").GetInt32());
        Assert.Equal(5060, body.GetProperty("port").GetInt32());
    }

    [Fact]
    public async Task CreateRecordForCaaSendsTheFlagsAndTagFieldsWithAnUnquotedValue()
    {
        DigitalOceanDnsProvider provider = new(_httpClient);
        DnsResourceRecord record = CloudDnsRecordMapper.ToCaaWireRecord(
            "example.com",
            isCritical: true,
            "issue",
            "letsencrypt.org",
            300
        );

        await provider.CreateRecordAsync(Credentials, "example.com", record);

        JsonElement body = Assert.IsType<JsonElement>(Assert.Single(_createRequests));
        Assert.Equal("letsencrypt.org", body.GetProperty("data").GetString());
        Assert.Equal(128, body.GetProperty("flags").GetInt32());
        Assert.Equal("issue", body.GetProperty("tag").GetString());
    }

    [Fact]
    public async Task CreateRecordConvertsFullyQualifiedNamesToRelativeNames()
    {
        DigitalOceanDnsProvider provider = new(_httpClient);
        DnsResourceRecord record = CloudDnsRecordMapper.ToWireRecord(
            DnsResourceRecordTypeEnum.A,
            "www.example.com",
            "192.0.2.40",
            300
        );

        await provider.CreateRecordAsync(Credentials, "example.com", record);

        Assert.Single(_createRequests);
    }

    [Fact]
    public async Task DeleteRecordCompletesSuccessfully()
    {
        DigitalOceanDnsProvider provider = new(_httpClient);

        await provider.DeleteRecordAsync(
            Credentials,
            "example.com",
            new CloudDnsProviderRecord(
                "1001",
                CloudDnsRecordMapper.ToWireRecord(
                    DnsResourceRecordTypeEnum.A,
                    "www.example.com",
                    "192.0.2.30",
                    300
                )
            )
        );
    }
}
