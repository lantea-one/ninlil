using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Rewrites;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class checks, on the shared in-process host (signed in as the bootstrap
///     administrator), that a CNAME rewrite answers an A query with the CNAME and the target's
///     address in one reply. It creates a primary zone with an A record for the target, turns
///     rewrites on through the options cache (as <see cref="FilterListEndToEndTests" /> does for
///     filtering), creates the rewrite through the API and sends an A query over UDP to the
///     host's Plain DNS listener. Names are unique per run, and the rewrite and zone are deleted
///     and the options restored in the <c>finally</c> block.
/// </summary>
[Collection(WebTestCollection.Name)]
public class RewriteCnameChaseEndToEndTests(NinlilDnsWebApplicationFactory factory)
{
    [Fact]
    public async Task ACnameRewrite_AnswersTheCnameAndTheTargetsAddress()
    {
        _ = factory.Server;

        HttpClient client = await CreateAuthenticatedClientAsync();
        string zoneName = $"chase-{Guid.NewGuid():N}.test";

        IOptionsMonitorCache<DnsRewriteOptions> optionsCache = factory.Services.GetRequiredService<
            IOptionsMonitorCache<DnsRewriteOptions>
        >();
        optionsCache.TryRemove(Microsoft.Extensions.Options.Options.DefaultName);
        optionsCache.TryAdd(
            Microsoft.Extensions.Options.Options.DefaultName,
            new DnsRewriteOptions { Enable = true }
        );

        Guid? zoneId = null;
        Guid? rewriteId = null;

        try
        {
            HttpResponseMessage zoneResponse = await client.PostAsJsonAsync(
                "/api/zones",
                new { name = zoneName, type = "Primary" }
            );
            zoneResponse.EnsureSuccessStatusCode();
            zoneId = (await zoneResponse.Content.ReadFromJsonAsync<ZoneDetail>()).Id;

            (
                await client.PostAsJsonAsync(
                    $"/api/zones/{zoneId}/records",
                    new
                    {
                        name = zoneName,
                        type = "SOA",
                        timeToLiveSeconds = 3600,
                        data = new
                        {
                            primaryNameServer = $"ns1.{zoneName}",
                            responsiblePersonMailbox = $"admin.{zoneName}",
                            serial = 1,
                            refreshSeconds = 3600,
                            retrySeconds = 600,
                            expireSeconds = 604800,
                            minimumTtlSeconds = 300,
                        },
                    }
                )
            ).EnsureSuccessStatusCode();
            (
                await client.PostAsJsonAsync(
                    $"/api/zones/{zoneId}/records",
                    new
                    {
                        name = $"target.{zoneName}",
                        type = "A",
                        timeToLiveSeconds = 300,
                        data = new { address = "192.0.2.9" },
                    }
                )
            ).EnsureSuccessStatusCode();

            HttpResponseMessage rewriteResponse = await client.PostAsJsonAsync(
                "/api/dns-rewrites",
                new { domain = $"www.{zoneName}", target = $"target.{zoneName}" }
            );
            rewriteResponse.EnsureSuccessStatusCode();
            rewriteId = (await rewriteResponse.Content.ReadFromJsonAsync<DnsRewriteDetail>()).Id;

            DnsMessage reply = await SendQueryAsync($"www.{zoneName}");

            Assert.Equal(DnsResponseCodeEnum.NoError, reply.ResponseCode);
            Assert.Equal(2, reply.Answers.Count);
            Assert.Equal(DnsResourceRecordTypeEnum.Cname, reply.Answers[0].Type);
            Assert.Equal(DnsResourceRecordTypeEnum.A, reply.Answers[1].Type);
            Assert.Equal(new byte[] { 192, 0, 2, 9 }, reply.Answers[1].Data);
        }
        finally
        {
            if (rewriteId is not null)
                await client.DeleteAsync($"/api/dns-rewrites/{rewriteId}");

            if (zoneId is not null)
                await client.DeleteAsync($"/api/zones/{zoneId}");

            optionsCache.TryRemove(Microsoft.Extensions.Options.Options.DefaultName);
        }
    }

    /// <summary>
    ///     This method sends one A query for <paramref name="name" /> to the host's Plain DNS
    ///     listener over UDP and returns the decoded reply.
    /// </summary>
    private async Task<DnsMessage> SendQueryAsync(string name)
    {
        DnsMessage query = new()
        {
            Id = (ushort)Random.Shared.Next(1, ushort.MaxValue),
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse(name),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };
        byte[] queryBytes = DnsMessageWriter.Write(query);

        using UdpClient udpClient = new(new IPEndPoint(IPAddress.Loopback, 0));
        await udpClient.SendAsync(
            queryBytes,
            queryBytes.Length,
            new IPEndPoint(IPAddress.Loopback, factory.PlainDnsPort)
        );
        UdpReceiveResult result = await udpClient
            .ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(20));
        return DnsMessageReader.Read(result.Buffer);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
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

        return client;
    }
}
