using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Filtering;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class checks that a filter-list change made through the REST API applies to the very
///     next DNS query, although the filter engine decides from in-memory copies. On the shared
///     in-process host (signed in as the bootstrap administrator) it turns filtering on, creates
///     a client group whose only network is one loopback address, a block list and the
///     assignment, sends a query over UDP from that address (which loads the copies), then adds a
///     block entry through the API and sends the same query again with no wait in between: the
///     second answer must be NXDOMAIN. Removing the entry must lift the block the same way.
///     <para>
///         All of <c>127.0.0.0/8</c> is loopback on Linux, so a <see cref="UdpClient" /> bound to
///         <c>127.0.0.12</c> sends from that address without raw sockets. The group matches only
///         that address, so no other test's query is filtered. The name is unique per run, so the
///         pipeline's response cache cannot hold an earlier answer for it. Filtering is turned
///         off again in the <c>finally</c> block, and the group and list are deleted.
///     </para>
/// </summary>
[Collection(WebTestCollection.Name)]
public class FilterListEndToEndTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     This field is the source address of the filtered client (see the class summary).
    /// </summary>
    private static readonly IPAddress FilteredAddress = IPAddress.Parse("127.0.0.12");

    [Fact]
    public async Task AnEntryAddedThroughTheApi_BlocksTheNextQueryAndRemovingItLiftsTheBlock()
    {
        // Reading Server starts the host and its hosted services, including the Plain DNS
        // listener.
        _ = factory.Server;

        HttpClient client = await CreateAuthenticatedClientAsync();
        string suffix = Guid.NewGuid().ToString("N");
        string blockedName = $"blocked-{suffix}.example.test";

        // The test host does not load the runtime settings file as a configuration source, so a
        // PATCH to /api/filtering-settings would not reach the live options. Putting an enabled
        // copy into the options cache does, and removing it in the finally block makes the
        // monitor build the configured (disabled) options again.
        IOptionsMonitorCache<DnsFilterOptions> optionsCache = factory.Services.GetRequiredService<
            IOptionsMonitorCache<DnsFilterOptions>
        >();
        optionsCache.TryRemove(Microsoft.Extensions.Options.Options.DefaultName);
        optionsCache.TryAdd(
            Microsoft.Extensions.Options.Options.DefaultName,
            new DnsFilterOptions
            {
                Enable = true,
                BlockResponseMode = DnsFilterBlockResponseModeEnum.NxDomain,
            }
        );

        Guid? groupId = null;
        Guid? listId = null;

        try
        {
            HttpResponseMessage groupResponse = await client.PostAsJsonAsync(
                "/api/client-groups",
                new { name = $"e2e-group-{suffix}" }
            );
            groupResponse.EnsureSuccessStatusCode();
            groupId = (await groupResponse.Content.ReadFromJsonAsync<ClientGroupDetail>()).Id;

            (
                await client.PostAsJsonAsync(
                    $"/api/client-groups/{groupId}/networks",
                    new { cidr = $"{FilteredAddress}/32" }
                )
            ).EnsureSuccessStatusCode();

            HttpResponseMessage listResponse = await client.PostAsJsonAsync(
                "/api/filter-lists",
                new
                {
                    name = $"e2e-list-{suffix}",
                    kind = "Domain",
                    action = "Block",
                    source = "Local",
                }
            );
            listResponse.EnsureSuccessStatusCode();
            listId = (await listResponse.Content.ReadFromJsonAsync<FilterListDetail>()).Id;

            (
                await client.PostAsJsonAsync(
                    $"/api/client-groups/{groupId}/list-assignments",
                    new { listId }
                )
            ).EnsureSuccessStatusCode();

            // The list is empty, so the name is not blocked. This query also loads the filter
            // copies, so the next one proves they were refreshed by the API write.
            Assert.NotEqual(DnsResponseCodeEnum.NxDomain, await SendQueryAsync(blockedName));

            HttpResponseMessage entryResponse = await client.PostAsJsonAsync(
                $"/api/filter-lists/{listId}/entries",
                new { value = blockedName }
            );
            entryResponse.EnsureSuccessStatusCode();
            FilterListEntryDetail entry =
                await entryResponse.Content.ReadFromJsonAsync<FilterListEntryDetail>();

            Assert.Equal(DnsResponseCodeEnum.NxDomain, await SendQueryAsync(blockedName));

            // A domain entry covers its subdomains, so this name is blocked too.
            string subdomain = $"www.{blockedName}";
            Assert.Equal(DnsResponseCodeEnum.NxDomain, await SendQueryAsync(subdomain));

            (
                await client.DeleteAsync($"/api/filter-lists/{listId}/entries/{entry.Id}")
            ).EnsureSuccessStatusCode();

            Assert.NotEqual(DnsResponseCodeEnum.NxDomain, await SendQueryAsync(subdomain));
        }
        finally
        {
            if (groupId is not null)
                await client.DeleteAsync($"/api/client-groups/{groupId}");

            if (listId is not null)
                await client.DeleteAsync($"/api/filter-lists/{listId}");

            optionsCache.TryRemove(Microsoft.Extensions.Options.Options.DefaultName);
        }
    }

    /// <summary>
    ///     This method sends one A query for <paramref name="name" /> from
    ///     <see cref="FilteredAddress" /> to the host's Plain DNS listener and returns the
    ///     response code.
    /// </summary>
    private async Task<DnsResponseCodeEnum> SendQueryAsync(string name)
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

        using UdpClient udpClient = new(new IPEndPoint(FilteredAddress, 0));
        await udpClient.SendAsync(
            queryBytes,
            queryBytes.Length,
            new IPEndPoint(IPAddress.Loopback, factory.PlainDnsPort)
        );

        // A name that is not blocked is refused or resolved, which can take as long as the
        // recursive timeout (15 seconds by default).
        UdpReceiveResult result = await udpClient
            .ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(20));

        return DnsMessageReader.Read(result.Buffer).ResponseCode;
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
