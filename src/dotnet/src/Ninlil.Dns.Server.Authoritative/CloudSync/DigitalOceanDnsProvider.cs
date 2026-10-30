using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CloudSync;

/// <summary>
///     This class implements <see cref="ICloudDnsProvider" /> with DigitalOcean's v2 REST API
///     (<c>https://api.digitalocean.com/v2</c>). DigitalOcean has no separate zone ID: the domain
///     name, stored in <c>ZoneEntity.CloudProviderZoneId</c>, is used in the API paths. Record
///     names are relative to the domain (<c>"@"</c> for the apex, <c>"www"</c> rather than
///     <c>"www.example.com"</c>), and this class converts them to and from fully qualified names.
///     <para>
///     As in <see cref="CloudflareDnsProvider" />, this class does not set
///     <see cref="HttpClient.BaseAddress" />; the client registration does.
///     </para>
/// </summary>
/// <param name="httpClient">The client every request is sent through.</param>
public sealed class DigitalOceanDnsProvider(HttpClient httpClient) : ICloudDnsProvider
{
    /// <summary>
    ///     This constant contains DigitalOcean's API base address.
    /// </summary>
    public const string DefaultBaseAddress = "https://api.digitalocean.com/v2/";

    /// <inheritdoc />
    public CloudDnsProviderTypeEnum ProviderType => CloudDnsProviderTypeEnum.DigitalOcean;

    /// <inheritdoc />
    public async Task<IReadOnlyList<CloudDnsProviderRecord>> ListRecordsAsync(
        IReadOnlyDictionary<string, string> credentials,
        string providerZoneId,
        CancellationToken stoppingToken = default
    )
    {
        List<CloudDnsProviderRecord> records = [];
        string nextPageUrl = $"domains/{providerZoneId}/records?per_page=100";

        while (nextPageUrl is not null)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, nextPageUrl);
            ApplyAuthorization(request, credentials);

            using HttpResponseMessage response = await httpClient.SendAsync(request, stoppingToken);
            response.EnsureSuccessStatusCode();

            DigitalOceanListResponse parsed =
                await response.Content.ReadFromJsonAsync<DigitalOceanListResponse>(stoppingToken);

            foreach (DigitalOceanRecord record in parsed?.DomainRecords ?? [])
            {
                if (
                    !TryParseType(record.Type, out DnsResourceRecordTypeEnum type)
                    || !CloudDnsSyncSupportedRecordTypes.Types.Contains(type)
                )
                    continue;

                string absoluteName = ToAbsoluteName(record.Name, providerZoneId);

                // DigitalOcean puts SRV (priority, weight, port) and CAA (flags, tag) fields in top-level
                // properties next to "data", unlike Cloudflare's nested object.
                DnsResourceRecord wireRecord = type switch
                {
                    DnsResourceRecordTypeEnum.Srv => CloudDnsRecordMapper.ToSrvWireRecord(
                        absoluteName,
                        (ushort)(record.Priority ?? 0),
                        (ushort)(record.Weight ?? 0),
                        (ushort)(record.Port ?? 0),
                        record.Data,
                        (uint)record.Ttl
                    ),
                    DnsResourceRecordTypeEnum.Caa => CloudDnsRecordMapper.ToCaaWireRecord(
                        absoluteName,
                        CaaFlagsToIsCritical(record.Flags),
                        record.Tag ?? string.Empty,
                        record.Data,
                        (uint)record.Ttl
                    ),
                    _ => CloudDnsRecordMapper.ToWireRecord(
                        type,
                        absoluteName,
                        record.Data,
                        (uint)record.Ttl,
                        (ushort)(record.Priority ?? 0)
                    ),
                };

                records.Add(new CloudDnsProviderRecord(record.Id.ToString(), wireRecord));
            }

            nextPageUrl = parsed?.Links?.Pages?.Next is { Length: > 0 } next
                ? StripApiPrefix(next)
                : null;
        }

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
        string data;
        int? priority = null;
        int? port = null;
        int? weight = null;
        int? flags = null;
        string tag = null;

        if (record.Type == DnsResourceRecordTypeEnum.Srv)
        {
            // For SRV, priority, weight and port are top-level fields and "data" holds only the target,
            // fully qualified with a trailing dot like other name values.
            (ushort recordPriority, ushort recordWeight, ushort recordPort, string target) =
                CloudDnsRecordMapper.FromSrvWireRecord(record);
            data = EnsureTrailingDot(target);
            priority = recordPriority;
            weight = recordWeight;
            port = recordPort;
        }
        else if (record.Type == DnsResourceRecordTypeEnum.Caa)
        {
            // For CAA, "data" holds the value (unquoted, unlike Route 53) and flags and tag are
            // top-level fields.
            (bool isCritical, string recordTag, string value) =
                CloudDnsRecordMapper.FromCaaWireRecord(record);
            data = value;
            flags = CaaIsCriticalToFlags(isCritical);
            tag = recordTag;
        }
        else
        {
            (string content, ushort mxPreference) = CloudDnsRecordMapper.FromWireRecord(record);

            bool isNameValued =
                record.Type
                is DnsResourceRecordTypeEnum.Cname
                    or DnsResourceRecordTypeEnum.NS
                    or DnsResourceRecordTypeEnum.MX;

            // DigitalOcean's documentation ("How to Create, Edit, and Delete DNS Records") says to
            // write a fully qualified domain name with a period at the end, which tells it from a
            // subdomain prefix.
            data = isNameValued ? EnsureTrailingDot(content) : content;
            priority = record.Type == DnsResourceRecordTypeEnum.MX ? mxPreference : null;
        }

        using HttpRequestMessage request = new(HttpMethod.Post, $"domains/{providerZoneId}/records")
        {
            Content = JsonContent.Create(
                new DigitalOceanCreateRequest(
                    record.Type.ToString().ToUpperInvariant(),
                    ToRelativeName(record.Name.ToString().TrimEnd('.'), providerZoneId),
                    data,
                    record.TimeToLive,
                    priority,
                    port,
                    weight,
                    flags,
                    tag
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
            $"domains/{providerZoneId}/records/{existingRecord.ProviderRecordId}"
        );
        ApplyAuthorization(request, credentials);

        using HttpResponseMessage response = await httpClient.SendAsync(request, stoppingToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    ///     This method converts a record name relative to its domain (<c>"@"</c> for the
    ///     apex) into Ninlil's always-fully-qualified presentation form.
    /// </summary>
    private static string ToAbsoluteName(string relativeName, string domain) =>
        relativeName is "@" or "" ? domain : $"{relativeName}.{domain}";

    /// <summary>
    ///     This method converts a fully qualified owner name to the domain-relative form
    ///     DigitalOcean expects.
    /// </summary>
    private static string ToRelativeName(string fullyQualifiedName, string domain) =>
        string.Equals(fullyQualifiedName, domain, StringComparison.OrdinalIgnoreCase)
            ? "@"
            : fullyQualifiedName[..^(domain.Length + 1)];

    /// <summary>
    ///     This method strips the <c>https://api.digitalocean.com/v2/</c> prefix from a pagination
    ///     link, since the client's base address already supplies it (and tests point it elsewhere).
    /// </summary>
    private static string StripApiPrefix(string absoluteUrl) =>
        absoluteUrl.Contains("/v2/")
            ? absoluteUrl[(absoluteUrl.IndexOf("/v2/") + 4)..]
            : absoluteUrl;

    /// <summary>
    ///     This method attaches DigitalOcean's bearer-token authorization header, from the
    ///     credential set's <c>ApiToken</c> field.
    /// </summary>
    private static void ApplyAuthorization(
        HttpRequestMessage request,
        IReadOnlyDictionary<string, string> credentials
    )
    {
        if (!credentials.TryGetValue("ApiToken", out string apiToken))
            throw new InvalidOperationException(
                "This credential set has no 'ApiToken' field, required for DigitalOcean."
            );

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            apiToken
        );
    }

    /// <summary>
    ///     This method parses DigitalOcean's record type string (for example, <c>"A"</c>)
    ///     into <see cref="DnsResourceRecordTypeEnum" />.
    /// </summary>
    private static bool TryParseType(string typeName, out DnsResourceRecordTypeEnum type) =>
        System.Enum.TryParse(typeName, ignoreCase: true, out type);

    /// <summary>
    ///     This method appends a trailing dot to a domain-name-valued field, matching
    ///     DigitalOcean's documented convention, unless it already has one.
    /// </summary>
    private static string EnsureTrailingDot(string name) => name.EndsWith('.') ? name : name + ".";

    /// <summary>
    ///     This method converts DigitalOcean's CAA <c>flags</c> value (0-255, RFC 8659 §4.1) to the
    ///     Critical bit <see cref="Ninlil.Dns.Common.Wire.Records.CaaRecordData" /> models, the only bit
    ///     RFC 8659 assigns.
    /// </summary>
    private static bool CaaFlagsToIsCritical(int? flags) => ((flags ?? 0) & 0x80) != 0;

    /// <summary>
    ///     This method reverses <see cref="CaaFlagsToIsCritical" />.
    /// </summary>
    private static int CaaIsCriticalToFlags(bool isCritical) => isCritical ? 0x80 : 0;

    /// <summary>
    ///     This record models the envelope DigitalOcean's <c>GET /records</c> endpoint wraps
    ///     its paged record list in.
    /// </summary>
    /// <param name="DomainRecords">This page's records.</param>
    /// <param name="Links">This page's pagination links.</param>
    private sealed record DigitalOceanListResponse(
        [property: JsonPropertyName("domain_records")] List<DigitalOceanRecord> DomainRecords,
        [property: JsonPropertyName("links")] DigitalOceanLinks Links
    );

    /// <summary>
    ///     This record models the pagination links DigitalOcean attaches to a
    ///     <see cref="DigitalOceanListResponse" />.
    /// </summary>
    /// <param name="Pages">The individual page links this response's links belong to.</param>
    private sealed record DigitalOceanLinks(
        [property: JsonPropertyName("pages")] DigitalOceanPages Pages
    );

    /// <summary>
    ///     This record models the "next page" link <see cref="ListRecordsAsync" /> follows until
    ///     it runs out of pages.
    /// </summary>
    /// <param name="Next">The next page's URL, or <see langword="null" /> on the last page.</param>
    private sealed record DigitalOceanPages([property: JsonPropertyName("next")] string Next);

    /// <summary>
    ///     This record models a single record as DigitalOcean's API represents it.
    /// </summary>
    /// <param name="Id">DigitalOcean's numeric identifier for this record.</param>
    /// <param name="Type">The record's type, as DigitalOcean's type name (for example, <c>"A"</c>).</param>
    /// <param name="Name">The record's owner name, relative to its domain.</param>
    /// <param name="Data">
    ///     The record's value; for SRV the target and for CAA the unquoted value. Their other fields
    ///     are in the properties below.
    /// </param>
    /// <param name="Ttl">The record's time-to-live, in seconds.</param>
    /// <param name="Priority">An MX or SRV record's priority value; <see langword="null" /> for every other type.</param>
    /// <param name="Port">An SRV record's port; <see langword="null" /> for every other type.</param>
    /// <param name="Weight">An SRV record's weight; <see langword="null" /> for every other type.</param>
    /// <param name="Flags">A CAA record's raw flags value (0-255, per RFC 8659 §4.1); <see langword="null" /> for every other type.</param>
    /// <param name="Tag">A CAA record's tag (for example, <c>issue</c>); <see langword="null" /> for every other type.</param>
    private sealed record DigitalOceanRecord(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("data")] string Data,
        [property: JsonPropertyName("ttl")] int Ttl,
        [property: JsonPropertyName("priority")] int? Priority,
        [property: JsonPropertyName("port")] int? Port,
        [property: JsonPropertyName("weight")] int? Weight,
        [property: JsonPropertyName("flags")] int? Flags,
        [property: JsonPropertyName("tag")] string Tag
    );

    /// <summary>
    ///     This record models the request body <see cref="CreateRecordAsync" /> posts to
    ///     DigitalOcean's <c>POST /records</c> endpoint.
    /// </summary>
    /// <param name="Type">The record's type, as DigitalOcean's type name (for example, <c>"A"</c>).</param>
    /// <param name="Name">The record's owner name, relative to its domain.</param>
    /// <param name="Data">The record's value; see <see cref="DigitalOceanRecord.Data" />.</param>
    /// <param name="Ttl">The record's time-to-live, in seconds.</param>
    /// <param name="Priority">An MX or SRV record's priority value; <see langword="null" /> for every other type.</param>
    /// <param name="Port">An SRV record's port; <see langword="null" /> for every other type.</param>
    /// <param name="Weight">An SRV record's weight; <see langword="null" /> for every other type.</param>
    /// <param name="Flags">A CAA record's raw flags value (0-255, per RFC 8659 §4.1); <see langword="null" /> for every other type.</param>
    /// <param name="Tag">A CAA record's tag (for example, <c>issue</c>); <see langword="null" /> for every other type.</param>
    private sealed record DigitalOceanCreateRequest(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("data")] string Data,
        [property: JsonPropertyName("ttl")] uint Ttl,
        [property: JsonPropertyName("priority")] int? Priority,
        [property: JsonPropertyName("port")] int? Port,
        [property: JsonPropertyName("weight")] int? Weight,
        [property: JsonPropertyName("flags")] int? Flags,
        [property: JsonPropertyName("tag")] string Tag
    );
}
