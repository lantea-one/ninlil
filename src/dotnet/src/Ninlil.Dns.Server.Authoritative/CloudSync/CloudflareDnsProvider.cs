using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CloudSync;

/// <summary>
///     This class implements <see cref="ICloudDnsProvider" /> with Cloudflare's v4 REST API
///     (<c>https://api.cloudflare.com/client/v4</c>). It does not set
///     <see cref="HttpClient.BaseAddress" />: <c>ServiceCollectionAuthoritativeExtension</c> sets the
///     real address when registering the client, and tests point it at a fake server that follows
///     Cloudflare's documented contract. It has not been tested against a live account.
/// </summary>
/// <param name="httpClient">The client every request is sent through.</param>
public sealed class CloudflareDnsProvider(HttpClient httpClient) : ICloudDnsProvider
{
    /// <summary>
    ///     This constant contains Cloudflare's API base address, which the HTTP client registration
    ///     uses; tests replace it with a fake server.
    /// </summary>
    public const string DefaultBaseAddress = "https://api.cloudflare.com/client/v4/";

    /// <inheritdoc />
    public CloudDnsProviderTypeEnum ProviderType => CloudDnsProviderTypeEnum.Cloudflare;

    /// <inheritdoc />
    public async Task<IReadOnlyList<CloudDnsProviderRecord>> ListRecordsAsync(
        IReadOnlyDictionary<string, string> credentials,
        string providerZoneId,
        CancellationToken stoppingToken = default
    )
    {
        List<CloudDnsProviderRecord> records = [];
        int page = 1;
        int totalPages;

        do
        {
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                $"zones/{providerZoneId}/dns_records?page={page}&per_page=100"
            );
            ApplyAuthorization(request, credentials);

            using HttpResponseMessage response = await httpClient.SendAsync(request, stoppingToken);
            response.EnsureSuccessStatusCode();

            CloudflareListResponse parsed =
                await response.Content.ReadFromJsonAsync<CloudflareListResponse>(stoppingToken);

            foreach (CloudflareRecord record in parsed?.Result ?? [])
            {
                if (
                    !TryParseType(record.Type, out DnsResourceRecordTypeEnum type)
                    || !CloudDnsSyncSupportedRecordTypes.Types.Contains(type)
                )
                    continue;

                DnsResourceRecord wireRecord = type switch
                {
                    // Cloudflare puts SRV and CAA fields in a nested "data" object instead of "content".
                    DnsResourceRecordTypeEnum.Srv => CloudDnsRecordMapper.ToSrvWireRecord(
                        record.Name,
                        (ushort)(record.Data?.Priority ?? 0),
                        (ushort)(record.Data?.Weight ?? 0),
                        (ushort)(record.Data?.Port ?? 0),
                        record.Data?.Target ?? string.Empty,
                        (uint)record.Ttl
                    ),
                    DnsResourceRecordTypeEnum.Caa => CloudDnsRecordMapper.ToCaaWireRecord(
                        record.Name,
                        CaaFlagsToIsCritical(record.Data?.Flags),
                        record.Data?.Tag ?? string.Empty,
                        record.Data?.Value ?? string.Empty,
                        (uint)record.Ttl
                    ),
                    _ => CloudDnsRecordMapper.ToWireRecord(
                        type,
                        record.Name,
                        record.Content,
                        (uint)record.Ttl,
                        (ushort)(record.Priority ?? 0)
                    ),
                };

                records.Add(new CloudDnsProviderRecord(record.Id, wireRecord));
            }

            totalPages = parsed?.ResultInfo?.TotalPages ?? 1;
            page++;
        } while (page <= totalPages);

        return records;
    }

    /// <inheritdoc />
    public async Task CreateRecordAsync(
        IReadOnlyDictionary<string, string> credentials,
        string providerZoneId,
        DnsResourceRecord record,
        CancellationToken stoppingToken = default
    )
    {
        // For SRV and CAA, Cloudflare computes "content" itself (read-only), so send the fields in
        // "data" instead.
        string content = null;
        int? mxPreference = null;
        CloudflareRecordData data = null;

        if (record.Type == DnsResourceRecordTypeEnum.Srv)
        {
            (ushort priority, ushort weight, ushort port, string target) =
                CloudDnsRecordMapper.FromSrvWireRecord(record);
            data = new CloudflareRecordData(priority, weight, port, target, null, null, null);
        }
        else if (record.Type == DnsResourceRecordTypeEnum.Caa)
        {
            (bool isCritical, string tag, string value) = CloudDnsRecordMapper.FromCaaWireRecord(
                record
            );
            data = new CloudflareRecordData(
                null,
                null,
                null,
                null,
                CaaIsCriticalToFlags(isCritical),
                tag,
                value
            );
        }
        else
        {
            (string plainContent, ushort preference) = CloudDnsRecordMapper.FromWireRecord(record);
            content = plainContent;
            mxPreference = record.Type == DnsResourceRecordTypeEnum.MX ? preference : null;
        }

        using HttpRequestMessage request = new(
            HttpMethod.Post,
            $"zones/{providerZoneId}/dns_records"
        )
        {
            Content = JsonContent.Create(
                new CloudflareCreateRequest(
                    record.Type.ToString().ToUpperInvariant(),
                    record.Name.ToString().TrimEnd('.'),
                    content,
                    (int)record.TimeToLive,
                    mxPreference,
                    data
                )
            ),
        };
        ApplyAuthorization(request, credentials);

        using HttpResponseMessage response = await httpClient.SendAsync(request, stoppingToken);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task DeleteRecordAsync(
        IReadOnlyDictionary<string, string> credentials,
        string providerZoneId,
        CloudDnsProviderRecord existingRecord,
        CancellationToken stoppingToken = default
    )
    {
        using HttpRequestMessage request = new(
            HttpMethod.Delete,
            $"zones/{providerZoneId}/dns_records/{existingRecord.ProviderRecordId}"
        );
        ApplyAuthorization(request, credentials);

        using HttpResponseMessage response = await httpClient.SendAsync(request, stoppingToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    ///     This method attaches Cloudflare's bearer-token authorization header, from the
    ///     credential set's <c>ApiToken</c> field.
    /// </summary>
    private static void ApplyAuthorization(
        HttpRequestMessage request,
        IReadOnlyDictionary<string, string> credentials
    )
    {
        if (!credentials.TryGetValue("ApiToken", out string apiToken))
            throw new InvalidOperationException(
                "This credential set has no 'ApiToken' field, required for Cloudflare."
            );

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            apiToken
        );
    }

    /// <summary>
    ///     This method parses Cloudflare's record type string (for example, <c>"A"</c>) into
    ///     <see cref="DnsResourceRecordTypeEnum" />.
    /// </summary>
    private static bool TryParseType(string typeName, out DnsResourceRecordTypeEnum type) =>
        System.Enum.TryParse(typeName, ignoreCase: true, out type);

    /// <summary>
    ///     This method converts Cloudflare's CAA <c>data.flags</c> value (0-255, RFC 8659 §4) to the
    ///     Critical bit <see cref="Ninlil.Dns.Common.Wire.Records.CaaRecordData" /> models, the only bit
    ///     RFC 8659 assigns.
    /// </summary>
    private static bool CaaFlagsToIsCritical(int? flags) => ((flags ?? 0) & 0x80) != 0;

    /// <summary>
    ///     This method reverses <see cref="CaaFlagsToIsCritical" />.
    /// </summary>
    private static int CaaIsCriticalToFlags(bool isCritical) => isCritical ? 0x80 : 0;

    /// <summary>
    ///     This record models the envelope Cloudflare's <c>GET /dns_records</c> endpoint
    ///     wraps its paged record list in.
    /// </summary>
    /// <param name="Result">This page's records.</param>
    /// <param name="ResultInfo">This page's pagination metadata.</param>
    private sealed record CloudflareListResponse(
        [property: JsonPropertyName("result")] List<CloudflareRecord> Result,
        [property: JsonPropertyName("result_info")] CloudflareResultInfo ResultInfo
    );

    /// <summary>
    ///     This record models the pagination metadata Cloudflare attaches to a
    ///     <see cref="CloudflareListResponse" />, used by <see cref="ListRecordsAsync" /> to know
    ///     when it has walked every page.
    /// </summary>
    /// <param name="TotalPages">The total number of pages this record set spans.</param>
    private sealed record CloudflareResultInfo(
        [property: JsonPropertyName("total_pages")] int TotalPages
    );

    /// <summary>
    ///     This record models a single record as Cloudflare's API represents it.
    /// </summary>
    /// <param name="Id">Cloudflare's opaque identifier for this record.</param>
    /// <param name="Type">The record's type, as Cloudflare's type name (for example, <c>"A"</c>).</param>
    /// <param name="Name">The record's owner name.</param>
    /// <param name="Content">The record's single value.</param>
    /// <param name="Ttl">The record's time-to-live, in seconds.</param>
    /// <param name="Priority">An MX record's preference value; <see langword="null" /> for every other type.</param>
    /// <param name="Data">
    ///     An SRV or CAA record's structured fields; <see langword="null" /> for every other
    ///     type, which instead carries its single value in <see cref="Content" />.
    /// </param>
    private sealed record CloudflareRecord(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("ttl")] int Ttl,
        [property: JsonPropertyName("priority")] int? Priority,
        [property: JsonPropertyName("data")] CloudflareRecordData Data
    );

    /// <summary>
    ///     This record models Cloudflare's nested <c>data</c> object, which holds the fields of an SRV
    ///     or CAA record; other types use the flat <c>content</c> and <c>priority</c> fields.
    /// </summary>
    /// <param name="Priority">An SRV record's priority.</param>
    /// <param name="Weight">An SRV record's weight.</param>
    /// <param name="Port">An SRV record's port.</param>
    /// <param name="Target">An SRV record's target host.</param>
    /// <param name="Flags">A CAA record's raw flags octet (0-255, per RFC 8659 §4).</param>
    /// <param name="Tag">A CAA record's tag (for example, <c>issue</c>).</param>
    /// <param name="Value">A CAA record's policy value.</param>
    private sealed record CloudflareRecordData(
        [property: JsonPropertyName("priority")] int? Priority,
        [property: JsonPropertyName("weight")] int? Weight,
        [property: JsonPropertyName("port")] int? Port,
        [property: JsonPropertyName("target")] string Target,
        [property: JsonPropertyName("flags")] int? Flags,
        [property: JsonPropertyName("tag")] string Tag,
        [property: JsonPropertyName("value")] string Value
    );

    /// <summary>
    ///     This record models the request body <see cref="CreateRecordAsync" /> posts to
    ///     Cloudflare's <c>POST /dns_records</c> endpoint.
    /// </summary>
    /// <param name="Type">The record's type, as Cloudflare's type name (for example, <c>"A"</c>).</param>
    /// <param name="Name">The record's owner name.</param>
    /// <param name="Content">
    ///     The record's value; <see langword="null" /> for SRV and CAA, which use <see cref="Data" />
    ///     (Cloudflare computes <c>content</c> for them).
    /// </param>
    /// <param name="Ttl">The record's time-to-live, in seconds.</param>
    /// <param name="Priority">An MX record's preference value; <see langword="null" /> for every other type.</param>
    /// <param name="Data">
    ///     An SRV or CAA record's structured fields; <see langword="null" /> for every other type.
    /// </param>
    private sealed record CloudflareCreateRequest(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("ttl")] int Ttl,
        [property: JsonPropertyName("priority")] int? Priority,
        [property: JsonPropertyName("data")] CloudflareRecordData Data
    );
}
