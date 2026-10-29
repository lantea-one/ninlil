using System.Text;
using Amazon;
using Amazon.Route53;
using Amazon.Route53.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Logging;
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
///     <see cref="FromRoute53Value" /> handle these formats.
///     </para>
///     <para>
///     A Route 53 record set holds all values of one name and type under one TTL (RFC 2181 §5.2).
///     <see cref="ListRecordsAsync" /> returns one record per value, all with the set's TTL and the
///     same ID (<c>name|TYPE</c>). <see cref="CreateRecordAsync" /> and
///     <see cref="DeleteRecordAsync" /> read the current set first and change it as a whole: CREATE
///     for a new set, UPSERT when values remain, DELETE with the set exactly as listed when the last
///     value goes, because Route 53 rejects a CREATE of an existing set and a DELETE that does not
///     match the set. The sync service runs zones one at a time, so the read and the change do not
///     race with each other.
///     </para>
///     <para>
///     Route 53 writes every octet outside <c>a-z 0-9 - _</c> in a name as a backslash and three
///     octal digits (<c>*</c> is <c>\052</c>). Names are decoded to presentation form on the way in
///     (<see cref="DecodeRoute53Name" />) and encoded the same way on the way out
///     (<see cref="EncodeRoute53Name" />). The same octal escapes appear inside values: the
///     Route 53 Developer Guide pages "DNS domain name format" and "Supported DNS record types"
///     describe only octal escapes, never RFC 1035 §5.1 decimal ones. Names inside CNAME, NS, MX and
///     SRV values use the name rules above, and TXT and CAA values use <see cref="DecodeRoute53Text" />
///     and <see cref="EncodeRoute53Text" />.
///     </para>
///     <para>
///     Alias record sets (<c>AliasTarget</c>) and routing-policy record sets (<c>SetIdentifier</c>:
///     weighted, latency, failover, geolocation, multi-value) cannot be represented as plain
///     records. They are never listed, and a create or delete for the same name and type does
///     nothing and logs a Warning, so a sync never overwrites them.
///     </para>
///     <para>
///     A <c>ServiceUrl</c> credential field replaces the AWS endpoint; tests use it to point at a
///     fake server that follows Route 53's documented contract. It has not been tested against a
///     live AWS account.
///     </para>
/// </summary>
/// <param name="logger">
///     The logger that records skipped alias and routing-policy record sets. With
///     <see langword="null" />, nothing is logged.
/// </param>
public sealed class Route53DnsProvider(ILogger<Route53DnsProvider> logger = null)
    : ICloudDnsProvider
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
                    !IsPlainRecordSet(recordSet)
                    || !TryParseType(recordSet.Type, out DnsResourceRecordTypeEnum type)
                    || !CloudDnsSyncSupportedRecordTypes.Types.Contains(type)
                )
                    continue;

                // Route 53 has no record ID; name and type identify a record set within a hosted zone, so
                // they are used as the ID. Every value of the set shares it.
                string ownerName = DecodeRoute53Name(recordSet.Name);

                foreach (ResourceRecord value in recordSet.ResourceRecords ?? [])
                    records.Add(
                        new CloudDnsProviderRecord(
                            $"{recordSet.Name}|{recordSet.Type}",
                            ParseRoute53Record(
                                type,
                                ownerName,
                                value.Value,
                                (uint)(recordSet.TTL ?? 0)
                            )
                        )
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
        string name = EncodeRoute53Name(record.Name);
        string type = record.Type.ToString().ToUpperInvariant();
        string newValue = ToRoute53Value(record);

        ResourceRecordSet existing = await FindRecordSetAsync(
            client,
            providerZoneId,
            name,
            record.Name,
            type,
            stoppingToken
        );

        if (existing is null)
        {
            await ChangeAsync(
                client,
                providerZoneId,
                ChangeAction.CREATE,
                name,
                type,
                record.TimeToLive,
                [newValue],
                stoppingToken
            );
            return;
        }

        if (!IsPlainRecordSet(existing))
        {
            logger?.LogWarning(
                "Route 53 record set '{Name}' ({Type}) in hosted zone '{ZoneId}' is an alias or routing-policy set; it is left alone and the local record is not created there.",
                existing.Name,
                type,
                providerZoneId
            );
            return;
        }

        List<string> values = [.. (existing.ResourceRecords ?? []).Select(value => value.Value)];
        bool present = values.Any(value => ValueMatches(record, value));

        if (present && existing.TTL == record.TimeToLive)
            return;

        if (!present)
            values.Add(newValue);

        // UPSERT carries the whole set; one TTL applies to all values (RFC 2181 §5.2).
        await ChangeAsync(
            client,
            providerZoneId,
            ChangeAction.UPSERT,
            existing.Name,
            type,
            record.TimeToLive,
            values,
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
        DnsResourceRecord record = existingRecord.Record;
        string type = record.Type.ToString().ToUpperInvariant();

        ResourceRecordSet existing = await FindRecordSetAsync(
            client,
            providerZoneId,
            EncodeRoute53Name(record.Name),
            record.Name,
            type,
            stoppingToken
        );

        if (existing is null)
            return;

        if (!IsPlainRecordSet(existing))
        {
            logger?.LogWarning(
                "Route 53 record set '{Name}' ({Type}) in hosted zone '{ZoneId}' is an alias or routing-policy set; it is left alone and no record is deleted from it.",
                existing.Name,
                type,
                providerZoneId
            );
            return;
        }

        List<string> values = [.. (existing.ResourceRecords ?? []).Select(value => value.Value)];
        List<string> remaining = [.. values.Where(value => !ValueMatches(record, value))];

        // The value is already gone, so there is nothing to change.
        if (remaining.Count == values.Count)
            return;

        // Route 53 accepts a DELETE only when it matches the set exactly (values and TTL), so the
        // last value goes with the set as listed; otherwise the rest stays under an UPSERT.
        await ChangeAsync(
            client,
            providerZoneId,
            remaining.Count == 0 ? ChangeAction.DELETE : ChangeAction.UPSERT,
            existing.Name,
            type,
            existing.TTL ?? 0,
            remaining.Count == 0 ? values : remaining,
            stoppingToken
        );
    }

    /// <summary>
    ///     This method reads the record set with the given name and type, the way Route 53 documents
    ///     it: ListResourceRecordSets starts at the name and type and returns the first set at or
    ///     after them, so the result is checked against both.
    /// </summary>
    /// <param name="client">The client to read with.</param>
    /// <param name="providerZoneId">The hosted zone's ID.</param>
    /// <param name="encodedName">The name in Route 53's escaped form, used as the start name.</param>
    /// <param name="name">The same name, used to compare with the returned set's decoded name.</param>
    /// <param name="type">The record type, in upper case.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The record set, or <see langword="null" /> when there is none.</returns>
    private static async Task<ResourceRecordSet> FindRecordSetAsync(
        AmazonRoute53Client client,
        string providerZoneId,
        string encodedName,
        DnsName name,
        string type,
        CancellationToken stoppingToken
    )
    {
        ListResourceRecordSetsResponse response = await client.ListResourceRecordSetsAsync(
            new ListResourceRecordSetsRequest
            {
                HostedZoneId = providerZoneId,
                StartRecordName = encodedName,
                StartRecordType = type,
                MaxItems = "1",
            },
            stoppingToken
        );

        ResourceRecordSet found = (response.ResourceRecordSets ?? []).FirstOrDefault();

        return
            found is not null
            && string.Equals(found.Type?.Value, type, StringComparison.OrdinalIgnoreCase)
            && DnsName.TryParse(DecodeRoute53Name(found.Name), out DnsName foundName)
            && foundName == name
            ? found
            : null;
    }

    /// <summary>
    ///     This method sends one change for one whole record set.
    /// </summary>
    private static Task ChangeAsync(
        AmazonRoute53Client client,
        string providerZoneId,
        ChangeAction action,
        string name,
        string type,
        long timeToLive,
        IEnumerable<string> values,
        CancellationToken stoppingToken
    ) =>
        client.ChangeResourceRecordSetsAsync(
            new ChangeResourceRecordSetsRequest
            {
                HostedZoneId = providerZoneId,
                ChangeBatch = new ChangeBatch
                {
                    Changes =
                    [
                        new Change
                        {
                            Action = action,
                            ResourceRecordSet = new ResourceRecordSet
                            {
                                Name = name,
                                Type = type,
                                TTL = timeToLive,
                                ResourceRecords =
                                [
                                    .. values.Select(value => new ResourceRecord { Value = value }),
                                ],
                            },
                        },
                    ],
                },
            },
            stoppingToken
        );

    /// <summary>
    ///     This method tells whether a record set is a plain one Ninlil can sync: not an alias and
    ///     without a set identifier (routing policies need one).
    /// </summary>
    private static bool IsPlainRecordSet(ResourceRecordSet recordSet) =>
        recordSet.AliasTarget is null && string.IsNullOrEmpty(recordSet.SetIdentifier);

    /// <summary>
    ///     This method tells whether a set's value string holds the same RDATA as a record. Parsing
    ///     makes quoting and trailing-dot variants match; the exact string is the fallback for a value
    ///     that does not parse.
    /// </summary>
    private static bool ValueMatches(DnsResourceRecord record, string rawValue)
    {
        if (string.Equals(rawValue, ToRoute53Value(record), StringComparison.Ordinal))
            return true;

        try
        {
            return ParseRoute53Record(record.Type, record.Name.ToString(), rawValue, 0)
                .Data.AsSpan()
                .SequenceEqual(record.Data);
        }
        catch (Exception exception)
            when (exception
                    is FormatException
                        or ArgumentException
                        or IndexOutOfRangeException
                        or OverflowException
                        or NotSupportedException
            )
        {
            return false;
        }
    }

    /// <summary>
    ///     This method turns a name from Route 53 into RFC 1035 §5.1 presentation form. Route 53
    ///     writes an octet outside <c>a-z 0-9 - _</c> as a backslash and three octal digits (the
    ///     Developer Guide's "DNS domain name format": <c>\052</c> is <c>*</c>). The octet is written
    ///     back as itself, as <c>\.</c> or <c>\</c> for a literal dot or backslash, or as three
    ///     decimal digits for a non-printable octet, which is what <see cref="DnsName.Parse" /> reads.
    /// </summary>
    private static string DecodeRoute53Name(string name)
    {
        StringBuilder builder = new();

        for (int i = 0; i < name.Length; i++)
        {
            if (
                name[i] == '\\'
                && i + 3 < name.Length
                && IsOctalDigit(name[i + 1])
                && IsOctalDigit(name[i + 2])
                && IsOctalDigit(name[i + 3])
                && Convert.ToInt32(name.Substring(i + 1, 3), 8) is int octet
                && octet <= 255
            )
            {
                if (octet is '.' or '\\')
                    builder.Append('\\').Append((char)octet);
                else if (octet is < 0x20 or > 0x7E)
                    builder.Append('\\').Append(octet.ToString("D3"));
                else
                    builder.Append((char)octet);

                i += 3;
            }
            else if (name[i] == '\\' && i + 1 < name.Length)
            {
                // Any other escape is already presentation form (for example an escaped dot); keep
                // both characters so the escaped character is not read as a separator.
                builder.Append(name[i]).Append(name[i + 1]);
                i++;
            }
            else
            {
                builder.Append(name[i]);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    ///     This method writes a name in Route 53's form: letters, digits, hyphen and underscore as
    ///     they are, every other octet (including a literal dot inside a label) as a backslash and
    ///     three octal digits, and a trailing dot.
    /// </summary>
    private static string EncodeRoute53Name(DnsName name)
    {
        if (name.IsRoot)
            return ".";

        StringBuilder builder = new();

        foreach (byte[] label in name.Labels)
        {
            foreach (byte octet in label)
            {
                if (
                    octet is >= (byte)'a' and <= (byte)'z'
                    || octet is >= (byte)'A' and <= (byte)'Z'
                    || octet is >= (byte)'0' and <= (byte)'9'
                    || octet is (byte)'-' or (byte)'_'
                )
                    builder.Append((char)octet);
                else
                    builder.Append('\\').Append(Convert.ToString(octet, 8).PadLeft(3, '0'));
            }

            builder.Append('.');
        }

        return builder.ToString();
    }

    /// <summary>
    ///     This method writes a name that sits inside a record value in Route 53's form, with the
    ///     trailing dot Route 53 expects (see <see cref="EncodeRoute53Name" />).
    /// </summary>
    private static string EncodeValueName(string name) =>
        EncodeRoute53Name(DnsName.Parse(EnsureTrailingDot(name)));

    /// <summary>
    ///     This method reads the text between the quotes of a TXT or CAA value into octets.
    ///     <c>\ddd</c> with three octal digits is that octet (the Developer Guide's TXT format, for
    ///     example <c>\344</c>), <c>\"</c> and <c>\\</c> are the quote and the backslash, and every other
    ///     character is its UTF-8 octets.
    /// </summary>
    private static byte[] DecodeRoute53Text(string text)
    {
        List<byte> octets = [];

        for (int i = 0; i < text.Length; i++)
        {
            if (
                text[i] == '\\'
                && i + 3 < text.Length
                && IsOctalDigit(text[i + 1])
                && IsOctalDigit(text[i + 2])
                && IsOctalDigit(text[i + 3])
                && Convert.ToInt32(text.Substring(i + 1, 3), 8) is int octet
                && octet <= 255
            )
            {
                octets.Add((byte)octet);
                i += 3;
            }
            else if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\')
            {
                octets.Add((byte)text[i + 1]);
                i++;
            }
            else
            {
                octets.AddRange(Encoding.UTF8.GetBytes(text[i].ToString()));
            }
        }

        return [.. octets];
    }

    /// <summary>
    ///     This method writes octets as the inside of a quoted TXT or CAA value: a quote or backslash
    ///     is escaped with a backslash, and a control octet or an octet from 0x7F up is a backslash and
    ///     three octal digits. A space stays as it is because it sits inside the quotes.
    /// </summary>
    private static string EncodeRoute53Text(ReadOnlySpan<byte> octets)
    {
        StringBuilder builder = new();

        foreach (byte octet in octets)
        {
            if (octet is (byte)'"' or (byte)'\\')
                builder.Append('\\').Append((char)octet);
            else if (octet < 0x20 || octet >= 0x7F)
                builder.Append('\\').Append(Convert.ToString(octet, 8).PadLeft(3, '0'));
            else
                builder.Append((char)octet);
        }

        return builder.ToString();
    }

    /// <summary>
    ///     This method splits TXT RDATA into its character-strings (RFC 1035 §3.3.14), so each one
    ///     can be quoted on its own in Route 53's value.
    /// </summary>
    private static List<byte[]> ReadTxtStrings(byte[] rdata)
    {
        List<byte[]> strings = [];

        for (int offset = 0; offset < rdata.Length; )
        {
            int length = Math.Min(rdata[offset], rdata.Length - offset - 1);
            strings.Add(rdata.AsSpan(offset + 1, length).ToArray());
            offset += 1 + length;
        }

        return strings;
    }

    /// <summary>
    ///     This method reads a Route 53 TXT value: quoted strings separated by white space, such as
    ///     <c>"String 1" "String 2"</c>, each with the escapes of <see cref="DecodeRoute53Text" />. A
    ///     value that does not start with a quote is taken as one unquoted string.
    /// </summary>
    private static List<byte[]> ParseRoute53TxtStrings(string value)
    {
        List<byte[]> strings = [];
        int i = 0;

        while (i < value.Length)
        {
            if (char.IsWhiteSpace(value[i]))
            {
                i++;
                continue;
            }

            if (value[i] != '"')
            {
                // Not a quoted string: keep the rest as one string, as Route 53 values written
                // without quotes carry no separators.
                strings.Add(DecodeRoute53Text(value[i..].TrimEnd()));
                break;
            }

            int start = ++i;

            // The closing quote is the first one not escaped by a backslash; an escaped
            // backslash (two of them) does not escape the quote after it.
            while (i < value.Length && value[i] != '"')
                i += value[i] == '\\' ? 2 : 1;

            int end = Math.Min(i, value.Length);
            strings.Add(DecodeRoute53Text(value[start..end]));
            i = end + 1;
        }

        return strings;
    }

    /// <summary>
    ///     This method writes strings as TXT RDATA, one character-string each; a string over 255
    ///     octets is split into several (RFC 1035 §3.3.14). With no strings, one empty
    ///     character-string.
    /// </summary>
    private static byte[] BuildTxtRdata(List<byte[]> strings)
    {
        List<byte> rdata = [];

        foreach (
            byte[] octets in strings.Count == 0
                ?
                [
                    [],
                ]
                : strings
        )
        {
            for (int offset = 0; offset == 0 || offset < octets.Length; offset += 255)
            {
                int length = Math.Min(255, octets.Length - offset);
                rdata.Add((byte)length);
                rdata.AddRange(octets.AsSpan(offset, length).ToArray());
            }
        }

        return [.. rdata];
    }

    /// <summary>
    ///     This method tells whether a character is an octal digit.
    /// </summary>
    private static bool IsOctalDigit(char c) => c is >= '0' and <= '7';

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

        // A TXT value is built from the raw character-strings, because the string the mapper
        // returns cannot hold an octet that is not valid UTF-8 and joins the strings. Route 53
        // takes one quoted string per character-string, separated by spaces, and rejects a
        // quoted string over 255 characters.
        if (record.Type == DnsResourceRecordTypeEnum.Txt)
            return string.Join(
                " ",
                ReadTxtStrings(record.Data).Select(octets => $"\"{EncodeRoute53Text(octets)}\"")
            );

        (string content, ushort mxPreference) = CloudDnsRecordMapper.FromWireRecord(record);

        return record.Type switch
        {
            DnsResourceRecordTypeEnum.MX => $"{mxPreference} {EncodeValueName(content)}",
            DnsResourceRecordTypeEnum.Cname or DnsResourceRecordTypeEnum.NS => EncodeValueName(
                content
            ),
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

        return $"{priority} {weight} {port} {EncodeValueName(target)}";
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

        return $"{(isCritical ? 128 : 0)} {tag} \"{EncodeRoute53Text(Encoding.UTF8.GetBytes(value))}\"";
    }

    /// <summary>
    ///     This method reverses <see cref="ToRoute53Value" /> for the single-value types except TXT;
    ///     SRV, CAA and TXT are parsed in <see cref="ParseRoute53Record" />.
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
                    DecodeRoute53Name(parts.Length > 1 ? parts[1] : value),
                    parts.Length > 1 && ushort.TryParse(parts[0], out ushort preference)
                        ? preference
                        : (ushort)0
                );
            case DnsResourceRecordTypeEnum.Cname:
            case DnsResourceRecordTypeEnum.NS:
                return (DecodeRoute53Name(value), (ushort)0);
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
    /// <param name="rawValue">One <c>Value</c> string of the record set.</param>
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
                DecodeRoute53Name(parts[3]),
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
                Encoding.UTF8.GetString(DecodeRoute53Text(value)),
                ttl
            );
        }

        if (type == DnsResourceRecordTypeEnum.Txt)
        {
            return new DnsResourceRecord(
                DnsName.Parse(name),
                type,
                DnsClassEnum.Internet,
                ttl,
                BuildTxtRdata(ParseRoute53TxtStrings(rawValue))
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
