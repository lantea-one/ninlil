using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Authoritative.CloudSync;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.CloudSync;

/// <summary>
///     This class covers <see cref="CloudflareDnsProvider" /> against a real, Kestrel-hosted fake
///     HTTP server standing in for Cloudflare's own documented v4 REST contract -- there is no
///     live Cloudflare account to verify against, so this is this project's own documented
///     verification boundary for this piece.
/// </summary>
public sealed class CloudflareDnsProviderTests : IAsyncLifetime
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
            "/zones/{zoneId}/dns_records",
            () =>
                Results.Json(
                    new
                    {
                        result = new object[]
                        {
                            new
                            {
                                id = "cf-record-1",
                                type = "A",
                                name = "www.example.com",
                                content = "192.0.2.10",
                                ttl = 300,
                                priority = (int?)null,
                            },
                            new
                            {
                                id = "cf-record-2",
                                type = "SRV",
                                name = "_sip._tcp.example.com",
                                // Cloudflare's own real "content" value for SRV/CAA is
                                // server-computed and formatted for display only -- the real
                                // structured fields this class actually maps arrive through
                                // "data" below, exactly as Cloudflare's own published schema
                                // documents.
                                content = "10 IN SRV 5 5060 sipserver.example.com.",
                                ttl = 300,
                                priority = (int?)null,
                                data = new
                                {
                                    priority = 10,
                                    weight = 5,
                                    port = 5060,
                                    target = "sipserver.example.com",
                                },
                            },
                            new
                            {
                                id = "cf-record-3",
                                type = "CAA",
                                name = "example.com",
                                content = "0 issue \"letsencrypt.org\"",
                                ttl = 300,
                                priority = (int?)null,
                                data = new
                                {
                                    flags = 0,
                                    tag = "issue",
                                    value = "letsencrypt.org",
                                },
                            },
                            new
                            {
                                id = "cf-record-4",
                                type = "PTR",
                                name = "10.2.0.192.in-addr.arpa",
                                content = "www.example.com",
                                ttl = 300,
                                priority = (int?)null,
                            },
                        },
                        result_info = new { total_pages = 1 },
                    }
                )
        );

        _fakeServer.MapPost(
            "/zones/{zoneId}/dns_records",
            async (HttpContext context) =>
            {
                object body = await context.Request.ReadFromJsonAsync<object>();
                _createRequests.Add(body);
                return Results.Json(new { success = true });
            }
        );

        _fakeServer.MapDelete(
            "/zones/{zoneId}/dns_records/{recordId}",
            () => Results.Json(new { success = true })
        );

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
    public async Task ListRecordsReturnsOnlySupportedRecordTypes()
    {
        CloudflareDnsProvider provider = new(_httpClient);

        IReadOnlyList<CloudDnsProviderRecord> records = await provider.ListRecordsAsync(
            Credentials,
            "zone-1"
        );

        // A/SRV/CAA are all supported; the fake server's own PTR record is not, and must be
        // silently skipped rather than causing a failure.
        Assert.Equal(3, records.Count);
        Assert.Contains(records, r => r.Record.Type == DnsResourceRecordTypeEnum.A);
        Assert.DoesNotContain(records, r => r.Record.Type == DnsResourceRecordTypeEnum.Ptr);
    }

    [Fact]
    public async Task ListRecordsParsesTheSrvDataObject()
    {
        CloudflareDnsProvider provider = new(_httpClient);

        IReadOnlyList<CloudDnsProviderRecord> records = await provider.ListRecordsAsync(
            Credentials,
            "zone-1"
        );

        CloudDnsProviderRecord srvRecord = Assert.Single(
            records,
            r => r.Record.Type == DnsResourceRecordTypeEnum.Srv
        );
        (ushort priority, ushort weight, ushort port, string target) =
            CloudDnsRecordMapper.FromSrvWireRecord(srvRecord.Record);

        Assert.Equal("cf-record-2", srvRecord.ProviderRecordId);
        Assert.Equal((ushort)10, priority);
        Assert.Equal((ushort)5, weight);
        Assert.Equal((ushort)5060, port);
        Assert.Equal("sipserver.example.com", target);
    }

    [Fact]
    public async Task ListRecordsParsesTheCaaDataObject()
    {
        CloudflareDnsProvider provider = new(_httpClient);

        IReadOnlyList<CloudDnsProviderRecord> records = await provider.ListRecordsAsync(
            Credentials,
            "zone-1"
        );

        CloudDnsProviderRecord caaRecord = Assert.Single(
            records,
            r => r.Record.Type == DnsResourceRecordTypeEnum.Caa
        );
        (bool isCritical, string tag, string value) = CloudDnsRecordMapper.FromCaaWireRecord(
            caaRecord.Record
        );

        Assert.Equal("cf-record-3", caaRecord.ProviderRecordId);
        Assert.False(isCritical);
        Assert.Equal("issue", tag);
        Assert.Equal("letsencrypt.org", value);
    }

    [Fact]
    public async Task CreateRecordForSrvSendsTheDataObject()
    {
        CloudflareDnsProvider provider = new(_httpClient);
        DnsResourceRecord record = CloudDnsRecordMapper.ToSrvWireRecord(
            "_sip._tcp.example.com",
            10,
            5,
            5060,
            "sipserver.example.com",
            300
        );

        await provider.CreateRecordAsync(Credentials, "zone-1", record);

        JsonElement body = Assert.IsType<JsonElement>(Assert.Single(_createRequests));
        JsonElement data = body.GetProperty("data");
        Assert.Equal(10, data.GetProperty("priority").GetInt32());
        Assert.Equal(5, data.GetProperty("weight").GetInt32());
        Assert.Equal(5060, data.GetProperty("port").GetInt32());
        Assert.Equal("sipserver.example.com", data.GetProperty("target").GetString());
    }

    [Fact]
    public async Task CreateRecordForCaaSendsTheDataObjectWithTheCriticalFlagBitSet()
    {
        CloudflareDnsProvider provider = new(_httpClient);
        DnsResourceRecord record = CloudDnsRecordMapper.ToCaaWireRecord(
            "example.com",
            isCritical: true,
            "issue",
            "letsencrypt.org",
            300
        );

        await provider.CreateRecordAsync(Credentials, "zone-1", record);

        JsonElement body = Assert.IsType<JsonElement>(Assert.Single(_createRequests));
        JsonElement data = body.GetProperty("data");
        Assert.Equal(128, data.GetProperty("flags").GetInt32());
        Assert.Equal("issue", data.GetProperty("tag").GetString());
        Assert.Equal("letsencrypt.org", data.GetProperty("value").GetString());
    }

    [Fact]
    public async Task CreateRecordSendsTheExpectedFields()
    {
        CloudflareDnsProvider provider = new(_httpClient);
        DnsResourceRecord record = CloudDnsRecordMapper.ToWireRecord(
            DnsResourceRecordTypeEnum.A,
            "www.example.com",
            "192.0.2.20",
            300
        );

        await provider.CreateRecordAsync(Credentials, "zone-1", record);

        Assert.Single(_createRequests);
    }

    [Fact]
    public async Task DeleteRecordCompletesSuccessfully()
    {
        CloudflareDnsProvider provider = new(_httpClient);

        await provider.DeleteRecordAsync(
            Credentials,
            "zone-1",
            new CloudDnsProviderRecord(
                "cf-record-1",
                CloudDnsRecordMapper.ToWireRecord(
                    DnsResourceRecordTypeEnum.A,
                    "www.example.com",
                    "192.0.2.10",
                    300
                )
            )
        );
    }

    [Fact]
    public async Task MissingApiTokenThrows()
    {
        CloudflareDnsProvider provider = new(_httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ListRecordsAsync(new Dictionary<string, string>(), "zone-1")
        );
    }
}
