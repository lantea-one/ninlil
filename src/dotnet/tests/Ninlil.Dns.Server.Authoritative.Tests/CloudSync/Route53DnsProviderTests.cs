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
///     This class covers <see cref="Route53DnsProvider" /> against a real, Kestrel-hosted,
///     hand-built minimal REST-XML fake standing in for Amazon Route 53's own documented API --
///     there is no live AWS account to verify against. The fake does not validate SigV4
///     authorization headers (the real <see cref="Amazon.Route53.AmazonRoute53Client" /> still
///     computes and sends them regardless; this fake simply does not check them), matching the
///     original throwaway smoke test's own approach that first proved this fake server design out.
/// </summary>
public sealed class Route53DnsProviderTests : IAsyncLifetime
{
    private WebApplication _fakeServer;
    private string _lastChangeRequestBody;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _fakeServer = builder.Build();

        _fakeServer.MapGet(
            "/2013-04-01/hostedzone/{zoneId}/rrset",
            () =>
                Results.Content(
                    """
                    <?xml version="1.0"?>
                    <ListResourceRecordSetsResponse xmlns="https://route53.amazonaws.com/doc/2013-04-01/">
                      <ResourceRecordSets>
                        <ResourceRecordSet>
                          <Name>www.example.com.</Name>
                          <Type>A</Type>
                          <TTL>300</TTL>
                          <ResourceRecords>
                            <ResourceRecord>
                              <Value>192.0.2.50</Value>
                            </ResourceRecord>
                          </ResourceRecords>
                        </ResourceRecordSet>
                        <ResourceRecordSet>
                          <Name>_sip._tcp.example.com.</Name>
                          <Type>SRV</Type>
                          <TTL>300</TTL>
                          <ResourceRecords>
                            <ResourceRecord>
                              <Value>10 5 5060 sipserver.example.com</Value>
                            </ResourceRecord>
                          </ResourceRecords>
                        </ResourceRecordSet>
                        <ResourceRecordSet>
                          <Name>example.com.</Name>
                          <Type>CAA</Type>
                          <TTL>300</TTL>
                          <ResourceRecords>
                            <ResourceRecord>
                              <Value>0 issue "letsencrypt.org"</Value>
                            </ResourceRecord>
                          </ResourceRecords>
                        </ResourceRecordSet>
                      </ResourceRecordSets>
                      <IsTruncated>false</IsTruncated>
                      <MaxItems>100</MaxItems>
                    </ListResourceRecordSetsResponse>
                    """,
                    "text/xml"
                )
        );

        _fakeServer.MapPost(
            "/2013-04-01/hostedzone/{zoneId}/rrset",
            async (HttpContext context) =>
            {
                using StreamReader reader = new(context.Request.Body);
                _lastChangeRequestBody = await reader.ReadToEndAsync();

                return Results.Content(
                    """
                    <?xml version="1.0"?>
                    <ChangeResourceRecordSetsResponse xmlns="https://route53.amazonaws.com/doc/2013-04-01/">
                      <ChangeInfo>
                        <Id>/change/C1</Id>
                        <Status>PENDING</Status>
                        <SubmittedAt>2026-01-01T00:00:00Z</SubmittedAt>
                      </ChangeInfo>
                    </ChangeResourceRecordSetsResponse>
                    """,
                    "text/xml"
                );
            }
        );

        await _fakeServer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _fakeServer.StopAsync();
        await _fakeServer.DisposeAsync();
    }

    private Dictionary<string, string> Credentials =>
        new()
        {
            ["AccessKeyId"] = "AKIAFAKE",
            ["SecretAccessKey"] = "fake-secret",
            ["ServiceUrl"] = _fakeServer.Urls.First(),
        };

    [Fact]
    public async Task ListRecordsParsesTheFakeXmlResponse()
    {
        Route53DnsProvider provider = new();

        IReadOnlyList<CloudDnsProviderRecord> records = await provider.ListRecordsAsync(
            Credentials,
            "Z12345"
        );

        Assert.Equal(3, records.Count);
        CloudDnsProviderRecord aRecord = Assert.Single(
            records,
            r => r.Record.Type == DnsResourceRecordTypeEnum.A
        );
        Assert.Equal("www.example.com.|A", aRecord.ProviderRecordId);
    }

    [Fact]
    public async Task ListRecordsParsesTheSpaceSeparatedSrvValue()
    {
        Route53DnsProvider provider = new();

        IReadOnlyList<CloudDnsProviderRecord> records = await provider.ListRecordsAsync(
            Credentials,
            "Z12345"
        );

        CloudDnsProviderRecord srvRecord = Assert.Single(
            records,
            r => r.Record.Type == DnsResourceRecordTypeEnum.Srv
        );
        (ushort priority, ushort weight, ushort port, string target) =
            CloudDnsRecordMapper.FromSrvWireRecord(srvRecord.Record);

        Assert.Equal("_sip._tcp.example.com.|SRV", srvRecord.ProviderRecordId);
        Assert.Equal((ushort)10, priority);
        Assert.Equal((ushort)5, weight);
        Assert.Equal((ushort)5060, port);
        Assert.Equal("sipserver.example.com", target);
    }

    [Fact]
    public async Task ListRecordsParsesTheQuotedCaaValue()
    {
        Route53DnsProvider provider = new();

        IReadOnlyList<CloudDnsProviderRecord> records = await provider.ListRecordsAsync(
            Credentials,
            "Z12345"
        );

        CloudDnsProviderRecord caaRecord = Assert.Single(
            records,
            r => r.Record.Type == DnsResourceRecordTypeEnum.Caa
        );
        (bool isCritical, string tag, string value) = CloudDnsRecordMapper.FromCaaWireRecord(
            caaRecord.Record
        );

        Assert.Equal("example.com.|CAA", caaRecord.ProviderRecordId);
        Assert.False(isCritical);
        Assert.Equal("issue", tag);
        Assert.Equal("letsencrypt.org", value);
    }

    [Fact]
    public async Task CreateRecordForSrvBuildsTheSpaceSeparatedValue()
    {
        Route53DnsProvider provider = new();
        DnsResourceRecord record = CloudDnsRecordMapper.ToSrvWireRecord(
            "_sip._tcp.example.com",
            10,
            5,
            5060,
            "sipserver.example.com",
            300
        );

        await provider.CreateRecordAsync(Credentials, "Z12345", record);

        Assert.NotNull(_lastChangeRequestBody);
        Assert.Contains("10 5 5060 sipserver.example.com.", _lastChangeRequestBody);
    }

    [Fact]
    public async Task CreateRecordForCaaBuildsTheQuotedValueWithTheCriticalFlagBitSet()
    {
        Route53DnsProvider provider = new();
        DnsResourceRecord record = CloudDnsRecordMapper.ToCaaWireRecord(
            "example.com",
            isCritical: true,
            "issue",
            "letsencrypt.org",
            300
        );

        await provider.CreateRecordAsync(Credentials, "Z12345", record);

        Assert.NotNull(_lastChangeRequestBody);
        Assert.Contains("128 issue \"letsencrypt.org\"", _lastChangeRequestBody);
    }

    [Fact]
    public async Task CreateRecordForMxPacksPriorityIntoTheSingleValueString()
    {
        Route53DnsProvider provider = new();
        DnsResourceRecord record = CloudDnsRecordMapper.ToWireRecord(
            DnsResourceRecordTypeEnum.MX,
            "example.com",
            "mail.example.com",
            300,
            10
        );

        await provider.CreateRecordAsync(Credentials, "Z12345", record);

        Assert.NotNull(_lastChangeRequestBody);
        Assert.Contains("10 mail.example.com.", _lastChangeRequestBody);
    }

    [Fact]
    public async Task CreateRecordForTxtWrapsTheValueInDoubleQuotes()
    {
        Route53DnsProvider provider = new();
        DnsResourceRecord record = CloudDnsRecordMapper.ToWireRecord(
            DnsResourceRecordTypeEnum.Txt,
            "example.com",
            "hello world",
            300
        );

        await provider.CreateRecordAsync(Credentials, "Z12345", record);

        Assert.Contains("\"hello world\"", _lastChangeRequestBody);
    }

    [Fact]
    public async Task DeleteRecordSendsADeleteChangeAction()
    {
        Route53DnsProvider provider = new();

        await provider.DeleteRecordAsync(
            Credentials,
            "Z12345",
            new CloudDnsProviderRecord(
                "www.example.com.|A",
                CloudDnsRecordMapper.ToWireRecord(
                    DnsResourceRecordTypeEnum.A,
                    "www.example.com",
                    "192.0.2.50",
                    300
                )
            )
        );

        Assert.Contains("DELETE", _lastChangeRequestBody);
    }
}
