using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CloudSync;

/// <summary>
///     This class implements <see cref="ICloudDnsProvider" /> against DigitalOcean's documented
///     v2 REST API (<c>https://api.digitalocean.com/v2</c>). Unlike Cloudflare and Route 53,
///     DigitalOcean has no separate opaque zone identifier -- the domain name itself, exactly
///     what <c>ZoneEntity.CloudProviderZoneId</c> holds for a DigitalOcean-backed zone, is both
///     the domain and the "zone ID" this provider's API paths are built from. This also means a
///     DigitalOcean record's own <c>name</c> field is relative to that domain (<c>"@"</c> for
///     the apex, <c>"www"</c> rather than <c>"www.example.com"</c>), which this class converts
///     to and from Ninlil's own always-fully-qualified owner names.
///     <para>
///         <paramref name="httpClient" />'s own <see cref="HttpClient.BaseAddress" /> is left
///         unset by this class deliberately, the same reasoning
///         <see cref="CloudflareDnsProvider" /> documents on itself.
///     </para>
/// </summary>
/// <param name="httpClient">The client every request is sent through.</param>
public sealed class DigitalOceanDnsProvider(HttpClient httpClient) : ICloudDnsProvider
{
    /// <summary>
    ///     This constant contains DigitalOcean's own real API base address.
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

                // DigitalOcean represents SRV/CAA through the same flat record shape as every
                // other type, but spreads their own fields (priority/weight/port for SRV,
                // flags/tag for CAA) across dedicated top-level properties alongside "data"
                // rather than packing them into "data" itself the way Cloudflare does -- see
                // this class's own doc comment.
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
            // DigitalOcean spreads an SRV record's own priority/weight/port across dedicated
            // top-level fields, with "data" carrying only the target -- the same fully-qualified,
            // trailing-dot convention as every other domain-name-valued type below.
            (ushort recordPriority, ushort recordWeight, ushort recordPort, string target) =
                CloudDnsRecordMapper.FromSrvWireRecord(record);
            data = EnsureTrailingDot(target);
            priority = recordPriority;
            weight = recordWeight;
            port = recordPort;
        }
        else if (record.Type == DnsResourceRecordTypeEnum.Caa)
        {
            // DigitalOcean carries a CAA record's own policy value in "data" (unquoted, unlike
            // Route 53's own single-string convention), with its flags/tag in their own
            // dedicated top-level fields.
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

            // DigitalOcean's own documented convention for a domain-name-valued record is a
            // fully-qualified value ending in a dot.
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
    ///     This method converts a record name relative to its own domain (<c>"@"</c> for the
    ///     apex) into Ninlil's own always-fully-qualified presentation form.
    /// </summary>
    private static string ToAbsoluteName(string relativeName, string domain) =>
        relativeName is "@" or "" ? domain : $"{relativeName}.{domain}";

    /// <summary>
    ///     This method converts a fully-qualified owner name back into the form relative to its
    ///     own domain that DigitalOcean's own API expects.
    /// </summary>
    private static string ToRelativeName(string fullyQualifiedName, string domain) =>
        string.Equals(fullyQualifiedName, domain, StringComparison.OrdinalIgnoreCase)
            ? "@"
            : fullyQualifiedName[..^(domain.Length + 1)];

    /// <summary>
    ///     This method strips DigitalOcean's own <c>https://api.digitalocean.com/v2/</c> prefix
    ///     off a pagination link, since <c>httpClient</c>'s configured base address
    ///     already supplies it (and, in this class's own smoke test, points somewhere else
    ///     entirely).
    /// </summary>
    private static string StripApiPrefix(string absoluteUrl) =>
        absoluteUrl.Contains("/v2/")
            ? absoluteUrl[(absoluteUrl.IndexOf("/v2/") + 4)..]
            : absoluteUrl;

    /// <summary>
    ///     This method attaches DigitalOcean's own bearer-token authorization header, from the
    ///     credential set's own <c>ApiToken</c> field.
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
    ///     This method parses DigitalOcean's own record type string (for example, <c>"A"</c>)
    ///     into <see cref="DnsResourceRecordTypeEnum" />.
    /// </summary>
    private static bool TryParseType(string typeName, out DnsResourceRecordTypeEnum type) =>
        System.Enum.TryParse(typeName, ignoreCase: true, out type);

    /// <summary>
    ///     This method appends a trailing dot to a domain-name-valued field, matching
    ///     DigitalOcean's own documented convention, unless it already has one.
    /// </summary>
    private static string EnsureTrailingDot(string name) => name.EndsWith('.') ? name : name + ".";

    /// <summary>
    ///     This method converts DigitalOcean's own raw CAA <c>flags</c> field (0-255, per RFC 8659
    ///     §4) into the single Critical bit <see cref="Ninlil.Dns.Common.Wire.Records.CaaRecordData" />
    ///     models -- the only bit that RFC currently assigns.
    /// </summary>
    private static bool CaaFlagsToIsCritical(int? flags) => ((flags ?? 0) & 0x80) != 0;

    /// <summary>
    ///     This method reverses <see cref="CaaFlagsToIsCritical" />.
    /// </summary>
    private static int CaaIsCriticalToFlags(bool isCritical) => isCritical ? 0x80 : 0;

    /// <summary>
    ///     This record models the envelope DigitalOcean's own <c>GET /records</c> endpoint wraps
    ///     its paged record list in.
    /// </summary>
    /// <param name="DomainRecords">This page's own records.</param>
    /// <param name="Links">This page's own pagination links.</param>
    private sealed record DigitalOceanListResponse(
        [property: JsonPropertyName("domain_records")] List<DigitalOceanRecord> DomainRecords,
        [property: JsonPropertyName("links")] DigitalOceanLinks Links
    );

    /// <summary>
    ///     This record models the pagination links DigitalOcean attaches to a
    ///     <see cref="DigitalOceanListResponse" />.
    /// </summary>
    /// <param name="Pages">The individual page links this response's own links belong to.</param>
    private sealed record DigitalOceanLinks(
        [property: JsonPropertyName("pages")] DigitalOceanPages Pages
    );

    /// <summary>
    ///     This record models the "next page" link <see cref="ListRecordsAsync" /> follows until
    ///     it runs out of pages.
    /// </summary>
    /// <param name="Next">The next page's own URL, or <see langword="null" /> on the last page.</param>
    private sealed record DigitalOceanPages([property: JsonPropertyName("next")] string Next);

    /// <summary>
    ///     This record models a single record as DigitalOcean's own API represents it.
    /// </summary>
    /// <param name="Id">DigitalOcean's own numeric identifier for this record.</param>
    /// <param name="Type">The record's type, as DigitalOcean's own type name (for example, <c>"A"</c>).</param>
    /// <param name="Name">The record's owner name, relative to its own domain.</param>
    /// <param name="Data">
    ///     The record's own single value -- an SRV record's own target, or a CAA record's own
    ///     policy value (unquoted); every other field that type carries lives in its own
    ///     dedicated property below rather than <c>Data</c>.
    /// </param>
    /// <param name="Ttl">The record's time-to-live, in seconds.</param>
    /// <param name="Priority">An MX or SRV record's own priority value; <see langword="null" /> for every other type.</param>
    /// <param name="Port">An SRV record's own port; <see langword="null" /> for every other type.</param>
    /// <param name="Weight">An SRV record's own weight; <see langword="null" /> for every other type.</param>
    /// <param name="Flags">A CAA record's own raw flags value (0-255, per RFC 8659 §4); <see langword="null" /> for every other type.</param>
    /// <param name="Tag">A CAA record's own tag (for example, <c>issue</c>); <see langword="null" /> for every other type.</param>
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
    ///     DigitalOcean's own <c>POST /records</c> endpoint.
    /// </summary>
    /// <param name="Type">The record's type, as DigitalOcean's own type name (for example, <c>"A"</c>).</param>
    /// <param name="Name">The record's owner name, relative to its own domain.</param>
    /// <param name="Data">The record's own single value -- see <see cref="DigitalOceanRecord.Data" />.</param>
    /// <param name="Ttl">The record's time-to-live, in seconds.</param>
    /// <param name="Priority">An MX or SRV record's own priority value; <see langword="null" /> for every other type.</param>
    /// <param name="Port">An SRV record's own port; <see langword="null" /> for every other type.</param>
    /// <param name="Weight">An SRV record's own weight; <see langword="null" /> for every other type.</param>
    /// <param name="Flags">A CAA record's own raw flags value (0-255, per RFC 8659 §4); <see langword="null" /> for every other type.</param>
    /// <param name="Tag">A CAA record's own tag (for example, <c>issue</c>); <see langword="null" /> for every other type.</param>
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
