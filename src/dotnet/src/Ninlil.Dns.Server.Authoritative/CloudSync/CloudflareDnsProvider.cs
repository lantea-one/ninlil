using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CloudSync;

/// <summary>
///     This class implements <see cref="ICloudDnsProvider" /> against Cloudflare's documented v4
///     REST API (<c>https://api.cloudflare.com/client/v4</c>). <paramref name="httpClient" />'s
///     own <see cref="HttpClient.BaseAddress" /> is left unset by this class deliberately: the
///     real Cloudflare API is the default (see <c>ServiceCollectionAuthoritativeExtension</c>'s
///     own <c>AddHttpClient</c> registration), and this class's own smoke test overrides it to
///     point at a fake HTTP server standing in for Cloudflare's own documented contract instead
///     -- there is no live Cloudflare account to verify against.
/// </summary>
/// <param name="httpClient">The client every request is sent through.</param>
public sealed class CloudflareDnsProvider(HttpClient httpClient) : ICloudDnsProvider
{
    /// <summary>
    ///     This constant contains Cloudflare's own real API base address, used whenever this
    ///     class's caller has not overridden <c>httpClient</c>'s own base address
    ///     (as this class's smoke test does, to point at a fake server instead).
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
                    // Cloudflare represents SRV/CAA as a nested "data" object of independent
                    // fields rather than one plain "content" string -- see this class's own
                    // doc comment.
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
        // Cloudflare's own "content" field is server-computed (readOnly) for SRV/CAA -- those
        // two publish their own value entirely through the nested "data" object instead, built
        // directly from the wire record's own structured fields rather than the generic
        // single-content-value path the other five supported types share.
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
    ///     This method attaches Cloudflare's own bearer-token authorization header, from the
    ///     credential set's own <c>ApiToken</c> field.
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
    ///     This method parses Cloudflare's own record type string (for example, <c>"A"</c>) into
    ///     <see cref="DnsResourceRecordTypeEnum" />.
    /// </summary>
    private static bool TryParseType(string typeName, out DnsResourceRecordTypeEnum type) =>
        System.Enum.TryParse(typeName, ignoreCase: true, out type);

    /// <summary>
    ///     This method converts Cloudflare's own raw CAA <c>data.flags</c> octet (0-255, per RFC
    ///     8659 §4) into the single Critical bit <see cref="Ninlil.Dns.Common.Wire.Records.CaaRecordData" />
    ///     models -- the only bit that RFC currently assigns.
    /// </summary>
    private static bool CaaFlagsToIsCritical(int? flags) => ((flags ?? 0) & 0x80) != 0;

    /// <summary>
    ///     This method reverses <see cref="CaaFlagsToIsCritical" />.
    /// </summary>
    private static int CaaIsCriticalToFlags(bool isCritical) => isCritical ? 0x80 : 0;

    /// <summary>
    ///     This record models the envelope Cloudflare's own <c>GET /dns_records</c> endpoint
    ///     wraps its paged record list in.
    /// </summary>
    /// <param name="Result">This page's own records.</param>
    /// <param name="ResultInfo">This page's own pagination metadata.</param>
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
    ///     This record models a single record as Cloudflare's own API represents it.
    /// </summary>
    /// <param name="Id">Cloudflare's own opaque identifier for this record.</param>
    /// <param name="Type">The record's type, as Cloudflare's own type name (for example, <c>"A"</c>).</param>
    /// <param name="Name">The record's owner name.</param>
    /// <param name="Content">The record's own single value.</param>
    /// <param name="Ttl">The record's time-to-live, in seconds.</param>
    /// <param name="Priority">An MX record's own preference value; <see langword="null" /> for every other type.</param>
    /// <param name="Data">
    ///     An SRV or CAA record's own structured fields; <see langword="null" /> for every other
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
    ///     This record models Cloudflare's own nested <c>data</c> object, which carries an SRV or
    ///     CAA record's own independent RDATA fields -- Cloudflare represents both types this way
    ///     rather than through the flat <c>content</c>/<c>priority</c> fields every other
    ///     supported type uses.
    /// </summary>
    /// <param name="Priority">An SRV record's own priority.</param>
    /// <param name="Weight">An SRV record's own weight.</param>
    /// <param name="Port">An SRV record's own port.</param>
    /// <param name="Target">An SRV record's own target host.</param>
    /// <param name="Flags">A CAA record's own raw flags octet (0-255, per RFC 8659 §4).</param>
    /// <param name="Tag">A CAA record's own tag (for example, <c>issue</c>).</param>
    /// <param name="Value">A CAA record's own policy value.</param>
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
    ///     Cloudflare's own <c>POST /dns_records</c> endpoint.
    /// </summary>
    /// <param name="Type">The record's type, as Cloudflare's own type name (for example, <c>"A"</c>).</param>
    /// <param name="Name">The record's owner name.</param>
    /// <param name="Content">
    ///     The record's own single value; left <see langword="null" /> for SRV/CAA, whose own
    ///     value is carried entirely through <see cref="Data" /> instead (Cloudflare computes
    ///     <c>content</c> itself for those two types).
    /// </param>
    /// <param name="Ttl">The record's time-to-live, in seconds.</param>
    /// <param name="Priority">An MX record's own preference value; <see langword="null" /> for every other type.</param>
    /// <param name="Data">
    ///     An SRV or CAA record's own structured fields; <see langword="null" /> for every other type.
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
