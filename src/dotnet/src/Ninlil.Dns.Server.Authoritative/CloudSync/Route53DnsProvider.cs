using Amazon;
using Amazon.Route53;
using Amazon.Route53.Model;
using Amazon.Runtime;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CloudSync;

/// <summary>
///     This class implements <see cref="ICloudDnsProvider" /> for Amazon Route 53 with the AWS
///     SDK's <see cref="AmazonRoute53Client" />, which handles SigV4 signing. The SDK is Apache-2.0
///     licensed; the clean-room rules in <c>NOTICE.md</c> concern Technitium and dnsmasq only.
///     <para>
///     Route 53 stores each value as one string whose format depends on the type: MX packs the
///     preference with the exchange, TXT is quoted, and SRV and CAA pack several space-separated
///     fields. <see cref="ToRoute53Value" />, <see cref="ParseRoute53Record" /> and
///     <see cref="FromRoute53Value" /> handle these formats. Each record set is treated as one
///     record (see <see cref="ListRecordsAsync" />).
///     </para>
///     <para>
///     A <c>ServiceUrl</c> credential field replaces the AWS endpoint; tests use it to point at a
///     fake server that follows Route 53's documented contract. It has not been tested against a
///     live AWS account.
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

            // The SDK can return null instead of an empty list, so guard against it.
            foreach (ResourceRecordSet recordSet in response.ResourceRecordSets ?? [])
            {
                if (
                    !TryParseType(recordSet.Type, out DnsResourceRecordTypeEnum type)
                    || !CloudDnsSyncSupportedRecordTypes.Types.Contains(type)
                )
                    continue;

                // A Route 53 record set can hold several values; only the first is read, so a set with more
                // values appears as one record.
                string rawValue = recordSet.ResourceRecords.FirstOrDefault()?.Value;

                if (rawValue is null)
                    continue;

                DnsResourceRecord wireRecord = ParseRoute53Record(
                    type,
                    recordSet.Name,
                    rawValue,
                    (uint)(recordSet.TTL ?? 0)
                );

                // Route 53 has no record ID; name and type identify a record set within a hosted zone, so
                // they are used as the ID.
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
    ///     This method builds a client from the credentials' <c>AccessKeyId</c> and
    ///     <c>SecretAccessKey</c>, sent to <c>ServiceUrl</c> instead of AWS when that field is set.
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
    ///     This method parses Route 53's record type string into <see cref="DnsResourceRecordTypeEnum" />.
    /// </summary>
    private static bool TryParseType(RRType rrType, out DnsResourceRecordTypeEnum type) =>
        System.Enum.TryParse(rrType.Value, ignoreCase: true, out type);

    /// <summary>
    ///     This method builds Route 53's value string for a record, using <see cref="BuildSrvValue" />
    ///     and <see cref="BuildCaaValue" /> for SRV and CAA.
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
            // Route 53 stores a TXT value as an RFC 1035 character-string in double quotes.
            DnsResourceRecordTypeEnum.Txt => $"\"{content}\"",
            _ => content,
        };
    }

    /// <summary>
    ///     This method builds Route 53's SRV value: priority, weight, port and target separated by
    ///     spaces, for example <c>10 5 80 hostname.example.com</c>.
    /// </summary>
    private static string BuildSrvValue(DnsResourceRecord record)
    {
        (ushort priority, ushort weight, ushort port, string target) =
            CloudDnsRecordMapper.FromSrvWireRecord(record);

        return $"{priority} {weight} {port} {EnsureTrailingDot(target)}";
    }

    /// <summary>
    ///     This method builds Route 53's CAA value: flags, tag and the quoted value, for example
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
    ///     This method reverses <see cref="ToRoute53Value" /> for the six single-value types; SRV and
    ///     CAA are parsed in <see cref="ParseRoute53Record" />.
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
    ///     This method parses a record set's <c>Value</c> string into a <see cref="DnsResourceRecord" />,
    ///     using the SRV and CAA mapper methods for those types and <see cref="FromRoute53Value" /> for
    ///     the rest.
    /// </summary>
    /// <param name="type">The record's type.</param>
    /// <param name="name">The record's owner name.</param>
    /// <param name="rawValue">The record set's first <c>Value</c> string.</param>
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
            // "<priority> <weight> <port> <target>", per Route 53's documented SRV format.
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
            // "<flags> <tag> \"<value>\"": split into at most 3 parts so spaces inside the value
            // (such as "ca.example.net; account=123456") are kept.
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
    ///     Route 53's documented convention, unless it already has one.
    /// </summary>
    private static string EnsureTrailingDot(string name) => name.EndsWith('.') ? name : name + ".";
}
