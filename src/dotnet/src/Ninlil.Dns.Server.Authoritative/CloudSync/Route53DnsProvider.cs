using Amazon;
using Amazon.Route53;
using Amazon.Route53.Model;
using Amazon.Runtime;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CloudSync;

/// <summary>
///     This class implements <see cref="ICloudDnsProvider" /> against Amazon Route 53, through
///     the official AWS SDK's own <see cref="AmazonRoute53Client" /> rather than hand-rolling AWS
///     SigV4 request signing -- Apache-2.0 licensed, so it carries none of this project's own
///     clean-room restrictions (those apply only to Technitium/dnsmasq, see <c>NOTICE.md</c>).
///     <para>
///         Route 53 represents every supported record type's own value as one plain string, but
///         that string's own shape differs by type in ways <see cref="CloudDnsRecordMapper" />
///         does not itself model (an MX record's priority is packed into the same string as its
///         exchange rather than a separate field; a TXT record's value is wrapped in literal
///         double quotes; an SRV record packs four space-separated fields into one string; a CAA
///         record packs its flags/tag/quoted-value the same way) -- handled here instead, in
///         <see cref="ToRoute53Value" />/<see cref="ParseRoute53Record" /> (and, for the six
///         simpler types, <see cref="FromRoute53Value" />).
///     </para>
///     <para>
///         A credential set's own <c>ServiceUrl</c> field, if present, overrides the real AWS
///         endpoint -- used only by this class's own smoke test, to point at a fake HTTP server
///         standing in for Route 53's documented contract instead; there is no live AWS account
///         to verify against.
///     </para>
/// </summary>
public sealed class Route53DnsProvider : ICloudDnsProvider
{
    /// <inheritdoc />
    public CloudDnsProviderTypeEnum ProviderType => CloudDnsProviderTypeEnum.Route53;

    /// <inheritdoc />
    public async Task<IReadOnlyList<CloudDnsProviderRecord>> ListRecordsAsync(
        IReadOnlyDictionary<string, string> credentials,
        string providerZoneId,
        CancellationToken stoppingToken = default
    )
    {
        using AmazonRoute53Client client = CreateClient(credentials);
        List<CloudDnsProviderRecord> records = [];

        ListResourceRecordSetsRequest request = new() { HostedZoneId = providerZoneId };

        while (true)
        {
            ListResourceRecordSetsResponse response = await client.ListResourceRecordSetsAsync(
                request,
                stoppingToken
            );

            // The AWS SDK can deserialize an empty/no-match result's collection properties as
            // null rather than an empty list, so this is null-guarded rather than trusted to
            // always be enumerable.
            foreach (ResourceRecordSet recordSet in response.ResourceRecordSets ?? [])
            {
                if (
                    !TryParseType(recordSet.Type, out DnsResourceRecordTypeEnum type)
                    || !CloudDnsSyncSupportedRecordTypes.Types.Contains(type)
                )
                    continue;

                // Route 53 can carry more than one value under the same name/type; this pass
                // publishes at most one wire record per Route 53 record set, taking the first
                // value -- a genuine, documented simplification, since neither
                // CloudDnsRecordMapper nor DnsResourceRecord model a multi-value record set as
                // anything other than several independent records.
                string rawValue = recordSet.ResourceRecords.FirstOrDefault()?.Value;

                if (rawValue is null)
                    continue;

                DnsResourceRecord wireRecord = ParseRoute53Record(
                    type,
                    recordSet.Name,
                    rawValue,
                    (uint)(recordSet.TTL ?? 0)
                );

                // Route 53 has no separate opaque per-record identifier the way Cloudflare and
                // DigitalOcean do; a record set's own name+type together already uniquely
                // identify it within a hosted zone, so that pair stands in for one here.
                records.Add(
                    new CloudDnsProviderRecord($"{recordSet.Name}|{recordSet.Type}", wireRecord)
                );
            }

            if (response.IsTruncated != true)
                break;

            request.StartRecordName = response.NextRecordName;
            request.StartRecordType = response.NextRecordType;
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
        using AmazonRoute53Client client = CreateClient(credentials);

        await client.ChangeResourceRecordSetsAsync(
            new ChangeResourceRecordSetsRequest
            {
                HostedZoneId = providerZoneId,
                ChangeBatch = new ChangeBatch
                {
                    Changes =
                    [
                        new Change
                        {
                            Action = ChangeAction.CREATE,
                            ResourceRecordSet = new ResourceRecordSet
                            {
                                Name = record.Name.ToString(),
                                Type = record.Type.ToString().ToUpperInvariant(),
                                TTL = record.TimeToLive,
                                ResourceRecords =
                                [
                                    new ResourceRecord { Value = ToRoute53Value(record) },
                                ],
                            },
                        },
                    ],
                },
            },
            stoppingToken
        );
    }

    /// <inheritdoc />
    public async Task DeleteRecordAsync(
        IReadOnlyDictionary<string, string> credentials,
        string providerZoneId,
        CloudDnsProviderRecord existingRecord,
        CancellationToken stoppingToken = default
    )
    {
        using AmazonRoute53Client client = CreateClient(credentials);

        await client.ChangeResourceRecordSetsAsync(
            new ChangeResourceRecordSetsRequest
            {
                HostedZoneId = providerZoneId,
                ChangeBatch = new ChangeBatch
                {
                    Changes =
                    [
                        new Change
                        {
                            Action = ChangeAction.DELETE,
                            ResourceRecordSet = new ResourceRecordSet
                            {
                                Name = existingRecord.Record.Name.ToString(),
                                Type = existingRecord.Record.Type.ToString().ToUpperInvariant(),
                                TTL = existingRecord.Record.TimeToLive,
                                ResourceRecords =
                                [
                                    new ResourceRecord
                                    {
                                        Value = ToRoute53Value(existingRecord.Record),
                                    },
                                ],
                            },
                        },
                    ],
                },
            },
            stoppingToken
        );
    }

    /// <summary>
    ///     This method builds a client authenticated against a credential set's own
    ///     <c>AccessKeyId</c>/<c>SecretAccessKey</c> fields, optionally redirected at a fake
    ///     endpoint via that same credential set's own <c>ServiceUrl</c> field.
    /// </summary>
    private static AmazonRoute53Client CreateClient(IReadOnlyDictionary<string, string> credentials)
    {
        if (
            !credentials.TryGetValue("AccessKeyId", out string accessKeyId)
            || !credentials.TryGetValue("SecretAccessKey", out string secretAccessKey)
        )
            throw new InvalidOperationException(
                "This credential set is missing 'AccessKeyId' and/or 'SecretAccessKey', required for Route 53."
            );

        AmazonRoute53Config config = new() { RegionEndpoint = RegionEndpoint.USEast1 };

        if (credentials.TryGetValue("ServiceUrl", out string serviceUrl) && serviceUrl.Length > 0)
        {
            config.ServiceURL = serviceUrl;
            config.UseHttp = serviceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        }

        return new AmazonRoute53Client(
            new BasicAWSCredentials(accessKeyId, secretAccessKey),
            config
        );
    }

    /// <summary>
    ///     This method parses Route 53's own record type string into <see cref="DnsResourceRecordTypeEnum" />.
    /// </summary>
    private static bool TryParseType(RRType rrType, out DnsResourceRecordTypeEnum type) =>
        System.Enum.TryParse(rrType.Value, ignoreCase: true, out type);

    /// <summary>
    ///     This method builds Route 53's own single-string representation of a record's value,
    ///     dispatching to <see cref="BuildSrvValue" />/<see cref="BuildCaaValue" /> for the two
    ///     record types that need more than the plain content/priority pair every other
    ///     supported type shares.
    /// </summary>
    private static string ToRoute53Value(DnsResourceRecord record)
    {
        if (record.Type == DnsResourceRecordTypeEnum.Srv)
            return BuildSrvValue(record);

        if (record.Type == DnsResourceRecordTypeEnum.Caa)
            return BuildCaaValue(record);

        (string content, ushort mxPreference) = CloudDnsRecordMapper.FromWireRecord(record);

        return record.Type switch
        {
            DnsResourceRecordTypeEnum.MX => $"{mxPreference} {EnsureTrailingDot(content)}",
            DnsResourceRecordTypeEnum.Cname or DnsResourceRecordTypeEnum.NS => EnsureTrailingDot(
                content
            ),
            // Route 53 represents a TXT record's value as an RFC 1035 <character-string>,
            // literally wrapped in double quotes, within the already-plain-string Value field.
            DnsResourceRecordTypeEnum.Txt => $"\"{content}\"",
            _ => content,
        };
    }

    /// <summary>
    ///     This method builds Route 53's own documented SRV value string: four space-separated
    ///     fields -- priority, weight, port, and target -- for example <c>10 5 80 hostname.example.com</c>.
    /// </summary>
    private static string BuildSrvValue(DnsResourceRecord record)
    {
        (ushort priority, ushort weight, ushort port, string target) =
            CloudDnsRecordMapper.FromSrvWireRecord(record);

        return $"{priority} {weight} {port} {EnsureTrailingDot(target)}";
    }

    /// <summary>
    ///     This method builds Route 53's own documented CAA value string: the raw flags octet,
    ///     the tag, and the value enclosed in double quotes -- for example
    ///     <c>0 issue "ca.example.net"</c>.
    /// </summary>
    private static string BuildCaaValue(DnsResourceRecord record)
    {
        (bool isCritical, string tag, string value) = CloudDnsRecordMapper.FromCaaWireRecord(
            record
        );

        return $"{(isCritical ? 128 : 0)} {tag} \"{value}\"";
    }

    /// <summary>
    ///     This method reverses <see cref="ToRoute53Value" /> for the six plain-content record
    ///     types it covers -- SRV/CAA are instead reversed directly by <see cref="ParseRoute53Record" />.
    /// </summary>
    private static (string Content, ushort MxPreference) FromRoute53Value(
        DnsResourceRecordTypeEnum type,
        string value
    )
    {
        switch (type)
        {
            case DnsResourceRecordTypeEnum.MX:
                string[] parts = value.Split(' ', 2);
                return (
                    parts.Length > 1 ? parts[1] : value,
                    parts.Length > 1 && ushort.TryParse(parts[0], out ushort preference)
                        ? preference
                        : (ushort)0
                );
            case DnsResourceRecordTypeEnum.Txt:
                return (
                    value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value,
                    (ushort)0
                );
            default:
                return (value, (ushort)0);
        }
    }

    /// <summary>
    ///     This method parses a record set's own raw Route 53 <c>Value</c> string back into a
    ///     full <see cref="DnsResourceRecord" />, dispatching SRV/CAA to their own dedicated
    ///     <see cref="CloudDnsRecordMapper" /> constructors rather than the shared
    ///     <see cref="CloudDnsRecordMapper.ToWireRecord" /> path <see cref="FromRoute53Value" />'s
    ///     own six plain-content types use.
    /// </summary>
    /// <param name="type">The record's type.</param>
    /// <param name="name">The record's owner name.</param>
    /// <param name="rawValue">The record set's own first <c>Value</c> string.</param>
    /// <param name="ttl">The record's time-to-live, in seconds.</param>
    private static DnsResourceRecord ParseRoute53Record(
        DnsResourceRecordTypeEnum type,
        string name,
        string rawValue,
        uint ttl
    )
    {
        if (type == DnsResourceRecordTypeEnum.Srv)
        {
            // "<priority> <weight> <port> <target>", per Route 53's own documented SRV format.
            string[] parts = rawValue.Split(' ', 4);
            return CloudDnsRecordMapper.ToSrvWireRecord(
                name,
                ushort.Parse(parts[0]),
                ushort.Parse(parts[1]),
                ushort.Parse(parts[2]),
                parts[3],
                ttl
            );
        }

        if (type == DnsResourceRecordTypeEnum.Caa)
        {
            // "<flags> <tag> \"<value>\"" -- split into exactly 3 parts so a value containing
            // its own embedded spaces (a real, valid CAA value, e.g.
            // "ca.example.net; account=123456") is not itself split apart.
            string[] parts = rawValue.Split(' ', 3);
            string quotedValue = parts[2];
            string value =
                quotedValue.Length >= 2 && quotedValue[0] == '"' && quotedValue[^1] == '"'
                    ? quotedValue[1..^1]
                    : quotedValue;

            return CloudDnsRecordMapper.ToCaaWireRecord(
                name,
                (byte.Parse(parts[0]) & 0x80) != 0,
                parts[1],
                value,
                ttl
            );
        }

        (string content, ushort mxPreference) = FromRoute53Value(type, rawValue);
        return CloudDnsRecordMapper.ToWireRecord(type, name, content, ttl, mxPreference);
    }

    /// <summary>
    ///     This method appends a trailing dot to a domain-name-valued content string, matching
    ///     Route 53's own documented convention, unless it already has one.
    /// </summary>
    private static string EnsureTrailingDot(string name) => name.EndsWith('.') ? name : name + ".";
}
