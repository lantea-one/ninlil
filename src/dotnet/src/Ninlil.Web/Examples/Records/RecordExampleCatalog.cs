using System.Text.Json;
using System.Text.Json.Nodes;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Examples.Records;

/// <summary>
///     This class carries one named example record, shared by
///     <see cref="RecordWriteRequestExample" /> and <see cref="RecordDetailExample" /> so both
///     show the same record under a given key in the Swagger UI example dropdown.
/// </summary>
/// <param name="Key">
///     The example's name in both dropdowns: the record type's mnemonic (<c>"A"</c>,
///     <c>"TLSA"</c> and so on), as an operator knows it from zone files and the admin console.
/// </param>
/// <param name="Name">The example record's owner name.</param>
/// <param name="Type">The example record's type.</param>
/// <param name="TimeToLiveSeconds">The example record's time-to-live.</param>
/// <param name="Data">The example record's type-specific data, already shaped as the API's camelCase JSON.</param>
/// <param name="Comments">Free-form operator notes for the example.</param>
/// <param name="Id">The example record's identifier, used only by the response examples.</param>
/// <param name="CreatedAtUtc">When the example record was created, used only by the response examples.</param>
/// <param name="DnsNameFields">
///     The top-level fields of <see cref="Data" /> backed by a
///     <c>Ninlil.Dns.Common.Wire.DnsName</c> in the record data model, or <see langword="null" /> when there are none. The API writes
///     such a field with a trailing dot (<c>DnsNameJsonConverter.Write</c>), so
///     <see cref="ResponseData" /> adds the dot to each of them for the response examples.
/// </param>
internal sealed record RecordExampleEntry(
    string Key,
    string Name,
    DnsResourceRecordTypeEnum Type,
    uint TimeToLiveSeconds,
    JsonElement Data,
    string Comments,
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<string> DnsNameFields = null
)
{
    /// <summary>
    ///     This method returns <see cref="Data" /> as the API reports it. Every field named in
    ///     <see cref="DnsNameFields" /> carries the trailing dot of a fully qualified name, which
    ///     is how <c>DnsNameJsonConverter.Write</c> writes a name. The record data model's enum
    ///     fields (<c>algorithm</c>, <c>typeCovered</c> and the <c>types</c> list) are written in
    ///     camelCase (<c>ecdsaP256Sha256</c>, <c>rrSig</c>), the form the API's serializer uses.
    ///     The request examples keep names and enums as an operator types them.
    /// </summary>
    public JsonElement ResponseData()
    {
        JsonObject data = JsonNode.Parse(Data.GetRawText()).AsObject();

        foreach (string field in DnsNameFields ?? [])
        {
            string name = data[field]?.GetValue<string>();
            if (name != null && !name.EndsWith('.'))
            {
                data[field] = name + ".";
            }
        }

        // Only a string is a DNSSEC algorithm name; the SSHFP and IPSECKEY algorithm fields are
        // numbers and stay as they are.
        if (
            data["algorithm"] is JsonValue algorithm
            && algorithm.GetValueKind() == JsonValueKind.String
        )
        {
            data["algorithm"] = CamelCaseEnum<DnssecAlgorithmEnum>(algorithm);
        }

        if (data["typeCovered"] != null)
        {
            data["typeCovered"] = CamelCaseEnum<DnsResourceRecordTypeEnum>(data["typeCovered"]);
        }

        if (data["types"] is JsonArray types)
        {
            data["types"] = new JsonArray([
                .. types.Select(type => CamelCaseEnum<DnsResourceRecordTypeEnum>(type)),
            ]);
        }

        return JsonSerializer.SerializeToElement(data);
    }

    /// <summary>
    ///     This method reads an enum value written the way the request examples write it (through
    ///     the enum's own converter) and returns it as the API's serializer writes it: the member
    ///     name in camelCase.
    /// </summary>
    private static JsonNode CamelCaseEnum<TEnum>(JsonNode node)
        where TEnum : struct, Enum =>
        JsonValue.Create(
            JsonNamingPolicy.CamelCase.ConvertName(
                JsonSerializer.Deserialize<TEnum>(node.ToJsonString()).ToString()
            )
        );
}

/// <summary>
///     This class is the single source of the 37 record type examples, one per type, so
///     <see cref="RecordWriteRequestExample" /> and <see cref="RecordDetailExample" /> cannot drift
///     apart: each maps this list into its own shape. All of them belong to the
///     <c>example.com</c> zone of <see cref="Zones.ZoneWriteRequestExample" /> and
///     <see cref="Zones.ZoneDetailExample" /> (<see cref="ZoneId" /> is the id of its "Primary,
///     DNSSEC-signed zone" example) and reuse that zone's DNSSEC algorithm
///     (<see cref="DnssecAlgorithmEnum.EcdsaP256Sha256" />) and NSEC3 parameters (0 iterations
///     and no salt, as RFC 9276 §3.1 recommends), so the examples across controllers describe one consistent deployment.
///     <para>
///         Every <see langword="byte[]" /> field (a digest, fingerprint, public key or signature)
///         is a computed value of the right length, mostly a SHA-1 or SHA-256 hash of a
///         descriptive string (20 bytes for SHA-1, 32 for SHA-256, 64 for a raw ECDSA P-256 point
///         per RFC 6605), because a wrong-length placeholder would look implausible to anyone who
///         knows the format. The bytes are serialized with System.Text.Json's default base64
///         handling (see <see cref="Data" />), the encoding the API reads, rather than written as
///         hand-computed base64 that could disagree with it.
///     </para>
/// </summary>
internal static class RecordExampleCatalog
{
    /// <summary>
    ///     This field contains the identifier of the zone every record example belongs to: the
    ///     <c>example.com</c> zone of the "Primary, DNSSEC-signed zone" example in
    ///     <see cref="Zones.ZoneDetailExample" />.
    /// </summary>
    private static readonly Guid ZoneId = Guid.Parse("1a2b3c4d-5e6f-4708-9182-a3b4c5d6e7f8");

    /// <summary>
    ///     This field contains the creation time every example record reports.
    /// </summary>
    private static readonly DateTimeOffset CreatedAtUtc = DateTimeOffset.Parse(
        "2026-08-15T10:00:00+00:00"
    );

    /// <summary>
    ///     This property exposes <see cref="ZoneId" /> to <see cref="RecordDetailExample" /> and
    ///     <see cref="RecordDetailPagedResultExample" />.
    /// </summary>
    public static Guid ExampleZoneId => ZoneId;

    /// <summary>
    ///     This method serializes an anonymous object into the <see cref="JsonElement" /> a
    ///     record's <c>data</c> field expects. No naming policy is applied, because the objects
    ///     below already use camelCase property names, and a <see langword="byte[]" /> becomes
    ///     base64, the System.Text.Json default the API relies on (the Angular side documents the
    ///     same in <c>record-data-encoding.util.ts</c>).
    /// </summary>
    private static JsonElement Data(object value) =>
        System.Text.Json.JsonSerializer.SerializeToElement(value);

    /// <summary>
    ///     This property contains the 37 example records, one for each record type
    ///     <c>Ninlil.Dns.Common.Wire.Records.DnsRecordDataCodecRegistry</c> has a codec for, in the
    ///     registry's order.
    /// </summary>
    public static IReadOnlyList<RecordExampleEntry> Entries { get; } =
    [
        new(
            Key: "A",
            Name: "www.example.com",
            Type: DnsResourceRecordTypeEnum.A,
            TimeToLiveSeconds: 3600,
            Data: Data(new { address = "203.0.113.10" }),
            Comments: "The public-facing web server's own IPv4 address.",
            Id: RecordId(1),
            CreatedAtUtc: CreatedAtUtc
        ),
        new(
            Key: "AAAA",
            Name: "www.example.com",
            Type: DnsResourceRecordTypeEnum.Aaaa,
            TimeToLiveSeconds: 3600,
            Data: Data(new { address = "2001:db8::10" }),
            Comments: "The public-facing web server's own IPv6 address.",
            Id: RecordId(2),
            CreatedAtUtc: CreatedAtUtc
        ),
        new(
            Key: "NS",
            Name: "example.com",
            Type: DnsResourceRecordTypeEnum.NS,
            TimeToLiveSeconds: 172800,
            Data: Data(new { nameServer = "ns1.example.com" }),
            Comments: "One of this zone's own authoritative name servers.",
            Id: RecordId(3),
            CreatedAtUtc: CreatedAtUtc,
            DnsNameFields: ["nameServer"]
        ),
        new(
            Key: "CNAME",
            Name: "blog.example.com",
            Type: DnsResourceRecordTypeEnum.Cname,
            TimeToLiveSeconds: 3600,
            Data: Data(new { target = "www.example.com" }),
            Comments: "An alias for the same web server the apex site is hosted on.",
            Id: RecordId(4),
            CreatedAtUtc: CreatedAtUtc,
            DnsNameFields: ["target"]
        ),
        new(
            Key: "SOA",
            Name: "example.com",
            Type: DnsResourceRecordTypeEnum.Soa,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    primaryNameServer = "ns1.example.com",
                    responsiblePersonMailbox = "admin.example.com",
                    serial = 2026083101u,
                    refreshSeconds = 3600u,
                    retrySeconds = 900u,
                    expireSeconds = 1209600u,
                    minimumTtlSeconds = 3600u,
                }
            ),
            Comments: "Every zone has exactly one of these, at its own apex.",
            Id: RecordId(5),
            CreatedAtUtc: CreatedAtUtc,
            DnsNameFields: ["primaryNameServer", "responsiblePersonMailbox"]
        ),
        new(
            Key: "PTR",
            Name: "10.2.0.203.in-addr.arpa",
            Type: DnsResourceRecordTypeEnum.Ptr,
            TimeToLiveSeconds: 3600,
            Data: Data(new { target = "www.example.com" }),
            Comments: "The reverse-lookup record for the web server's own 203.0.113.10 address.",
            Id: RecordId(6),
            CreatedAtUtc: CreatedAtUtc,
            DnsNameFields: ["target"]
        ),
        new(
            Key: "MX",
            Name: "example.com",
            Type: DnsResourceRecordTypeEnum.MX,
            TimeToLiveSeconds: 3600,
            Data: Data(new { preference = 10, exchange = "mail.example.com" }),
            Comments: "This domain's own primary inbound mail server.",
            Id: RecordId(7),
            CreatedAtUtc: CreatedAtUtc,
            DnsNameFields: ["exchange"]
        ),
        new(
            Key: "TXT",
            Name: "example.com",
            Type: DnsResourceRecordTypeEnum.Txt,
            TimeToLiveSeconds: 3600,
            Data: Data(new { values = new[] { "v=spf1 include:_spf.example.com ~all" } }),
            Comments: "This domain's own SPF policy.",
            Id: RecordId(8),
            CreatedAtUtc: CreatedAtUtc
        ),
        new(
            Key: "SRV",
            Name: "_sip._tcp.example.com",
            Type: DnsResourceRecordTypeEnum.Srv,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    priority = 10,
                    weight = 60,
                    port = 5060,
                    target = "sipserver.example.com",
                }
            ),
            Comments: "The SIP server responsible for this domain's own voice service.",
            Id: RecordId(9),
            CreatedAtUtc: CreatedAtUtc,
            DnsNameFields: ["target"]
        ),
        new(
            Key: "CAA",
            Name: "example.com",
            Type: DnsResourceRecordTypeEnum.Caa,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    isCritical = false,
                    tag = "issue",
                    value = "letsencrypt.org",
                }
            ),
            Comments: "Restricts certificate issuance for this domain to Let's Encrypt -- the same issuer domain this zone's own CAA auto-management targets.",
            Id: RecordId(10),
            CreatedAtUtc: CreatedAtUtc
        ),
        new(
            Key: "DNAME",
            Name: "old.example.com",
            Type: DnsResourceRecordTypeEnum.Dname,
            TimeToLiveSeconds: 3600,
            Data: Data(new { target = "new-location.example.com" }),
            Comments: "Redirects the whole old.example.com subtree to its new location at once.",
            Id: RecordId(11),
            CreatedAtUtc: CreatedAtUtc,
            DnsNameFields: ["target"]
        ),
        new(
            Key: "SVCB",
            Name: "_dns.example.com",
            Type: DnsResourceRecordTypeEnum.Svcb,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    priority = 1,
                    targetName = "dns.example.com",
                    // Parameter key 1 (alpn), value 0x03 'd' 'o' 't': one 3-byte ALPN identifier
                    // ("dot") in the length-prefixed encoding of RFC 9460 §7.1.1.
                    parameters = new[]
                    {
                        new { key = 1, value = Convert.FromHexString("03646f74") },
                    },
                }
            ),
            Comments: "Advertises this domain's own DNS-over-TLS resolver endpoint.",
            Id: RecordId(12),
            CreatedAtUtc: CreatedAtUtc,
            DnsNameFields: ["targetName"]
        ),
        new(
            Key: "HTTPS",
            Name: "www.example.com",
            Type: DnsResourceRecordTypeEnum.Https,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    priority = 1,
                    targetName = "www.example.com",
                    parameters = new[]
                    {
                        // Parameter key 1 (alpn): two length-prefixed ALPN identifiers, "h2"
                        // and "h3", per RFC 9460 §7.1.1.
                        new { key = 1, value = Convert.FromHexString("026832026833") },
                        // Parameter key 3 (port): the 2-byte big-endian alternate port 443.
                        new { key = 3, value = Convert.FromHexString("01bb") },
                    },
                }
            ),
            Comments: "Lets a browser discover HTTP/2 and HTTP/3 support for this web server before ever opening a connection.",
            Id: RecordId(37),
            CreatedAtUtc: CreatedAtUtc,
            DnsNameFields: ["targetName"]
        ),
        new(
            Key: "NAPTR",
            Name: "example.com",
            Type: DnsResourceRecordTypeEnum.Naptr,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    order = 100,
                    preference = 10,
                    flags = "S",
                    services = "SIP+D2U",
                    regularExpression = "",
                    replacement = "_sip._udp.example.com",
                }
            ),
            Comments: "An ENUM/SIP-style rewrite rule pointing at this domain's own SIP service.",
            Id: RecordId(13),
            CreatedAtUtc: CreatedAtUtc,
            DnsNameFields: ["replacement"]
        ),
        new(
            Key: "LOC",
            Name: "example.com",
            Type: DnsResourceRecordTypeEnum.Loc,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    latitudeDegrees = 37.775,
                    longitudeDegrees = -122.4194,
                    altitudeMeters = 16.0,
                    diameterMeters = 1.0,
                    horizontalPrecisionMeters = 10000.0,
                    verticalPrecisionMeters = 10.0,
                }
            ),
            Comments: "This domain's own operator's approximate physical location.",
            Id: RecordId(14),
            CreatedAtUtc: CreatedAtUtc
        ),
        new(
            Key: "URI",
            Name: "_ftp.example.com",
            Type: DnsResourceRecordTypeEnum.Uri,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    priority = 10,
                    weight = 1,
                    target = "ftp://ftp.example.com/",
                }
            ),
            Comments: "Points this domain's own FTP service alias at its real endpoint.",
            Id: RecordId(15),
            CreatedAtUtc: CreatedAtUtc
        ),
        new(
            Key: "HINFO",
            Name: "example.com",
            Type: DnsResourceRecordTypeEnum.Hinfo,
            TimeToLiveSeconds: 3600,
            Data: Data(new { cpu = "GENERIC-PC", operatingSystem = "LINUX" }),
            Comments: "This project's own minimal answer to an ANY query at this name, per RFC 8482.",
            Id: RecordId(16),
            CreatedAtUtc: CreatedAtUtc
        ),
        new(
            Key: "RP",
            Name: "example.com",
            Type: DnsResourceRecordTypeEnum.RP,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new { mailbox = "admin.example.com", textReference = "admin-info.example.com" }
            ),
            Comments: "Names this domain's own responsible person and where to find more information about them.",
            Id: RecordId(17),
            CreatedAtUtc: CreatedAtUtc,
            DnsNameFields: ["mailbox", "textReference"]
        ),
        new(
            Key: "KX",
            Name: "example.com",
            Type: DnsResourceRecordTypeEnum.KX,
            TimeToLiveSeconds: 3600,
            Data: Data(new { preference = 10, exchanger = "keyserver.example.com" }),
            Comments: "The host responsible for key exchange on this domain's own behalf.",
            Id: RecordId(18),
            CreatedAtUtc: CreatedAtUtc,
            DnsNameFields: ["exchanger"]
        ),
        new(
            Key: "AFSDB",
            Name: "example.com",
            Type: DnsResourceRecordTypeEnum.Afsdb,
            TimeToLiveSeconds: 3600,
            Data: Data(new { subtype = 1, hostname = "afsdb.example.com" }),
            Comments: "Locates this domain's own AFS cell database server.",
            Id: RecordId(19),
            CreatedAtUtc: CreatedAtUtc,
            DnsNameFields: ["hostname"]
        ),
        new(
            Key: "APL",
            Name: "example.com",
            Type: DnsResourceRecordTypeEnum.Apl,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    items = new object[]
                    {
                        // A /24 IPv4 prefix carries only its three significant octets (192, 0, 2),
                        // not all four (see AplItem).
                        new
                        {
                            addressFamily = 1,
                            prefixLength = 24,
                            negated = false,
                            addressFamilyDependentPart = new byte[] { 192, 0, 2 },
                        },
                        // A /32 IPv4 prefix carries all four octets, and "negated" excludes this
                        // host from the network above.
                        new
                        {
                            addressFamily = 1,
                            prefixLength = 32,
                            negated = true,
                            addressFamilyDependentPart = new byte[] { 203, 0, 113, 5 },
                        },
                    },
                }
            ),
            Comments: "A permitted /24 prefix, with one specific address inside it explicitly excluded.",
            Id: RecordId(20),
            CreatedAtUtc: CreatedAtUtc
        ),
        new(
            Key: "EUI48",
            Name: "printer.example.com",
            Type: DnsResourceRecordTypeEnum.Eui48,
            TimeToLiveSeconds: 3600,
            Data: Data(new { identifier = Convert.FromHexString("001A2B3C4D5E") }),
            Comments: "The network printer's own 48-bit MAC address.",
            Id: RecordId(21),
            CreatedAtUtc: CreatedAtUtc
        ),
        new(
            Key: "EUI64",
            Name: "sensor.example.com",
            Type: DnsResourceRecordTypeEnum.Eui64,
            TimeToLiveSeconds: 3600,
            Data: Data(new { identifier = Convert.FromHexString("001A2B3C4D5E6F70") }),
            Comments: "A 64-bit extended unique identifier for a network-attached sensor.",
            Id: RecordId(22),
            CreatedAtUtc: CreatedAtUtc
        ),
        new(
            Key: "SSHFP",
            Name: "ssh.example.com",
            Type: DnsResourceRecordTypeEnum.SshFp,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    // Algorithm 4 (Ed25519, RFC 7479) and fingerprint type 2 (SHA-256, RFC 6594),
                    // so the fingerprint is a 32-byte SHA-256 digest.
                    algorithm = 4,
                    fingerprintType = 2,
                    fingerprint = Convert.FromHexString(
                        "afa6c7ed5f80c9510cfbf4f821fe45196f60eec21efc1a45db3a8a599cc4c2bf"
                    ),
                }
            ),
            Comments: "Lets an SSH client verify this host's own Ed25519 host key out-of-band via DNSSEC.",
            Id: RecordId(23),
            CreatedAtUtc: CreatedAtUtc
        ),
        new(
            Key: "TLSA",
            Name: "_443._tcp.www.example.com",
            Type: DnsResourceRecordTypeEnum.Tlsa,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    // Usage 1 (PKIX-EE), selector 1 (subject public key), matching type 1
                    // (SHA-256), so the data is a 32-byte SHA-256 digest.
                    certificateUsage = 1,
                    selector = 1,
                    matchingType = 1,
                    certificateAssociationData = Convert.FromHexString(
                        "ffaecbd53defa969e57ad16de023bd6214e6e950ab7b7e9f115479808cc2a37b"
                    ),
                }
            ),
            Comments: "Pins the TLS certificate this web server presents on port 443, anchored by DNSSEC.",
            Id: RecordId(24),
            CreatedAtUtc: CreatedAtUtc
        ),
        new(
            Key: "SMIMEA",
            Name: "c93f1e400f26708f98cb19d936620da35eec8f72e57f9eec01c1afd6._smimecert.example.com",
            Type: DnsResourceRecordTypeEnum.SmimeA,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    certificateUsage = 3,
                    selector = 1,
                    matchingType = 1,
                    certificateAssociationData = Convert.FromHexString(
                        "40d4e2b85caf75be2082081040752ae94c438c8494512494c2dfb9e68aa880f6"
                    ),
                }
            ),
            Comments: "Pins alice@example.com's own S/MIME certificate -- owner name per RFC 8162's own hashed-local-part convention.",
            Id: RecordId(25),
            CreatedAtUtc: CreatedAtUtc
        ),
        new(
            Key: "OPENPGPKEY",
            Name: "c93f1e400f26708f98cb19d936620da35eec8f72e57f9eec01c1afd6._openpgpkey.example.com",
            Type: DnsResourceRecordTypeEnum.OpenPgpKey,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    keyData = Convert.FromHexString(
                        "633a355f9e1c831273260df697b847d660957510f1c01bb20aff0118e997bc73"
                    ),
                }
            ),
            Comments: "alice@example.com's own OpenPGP public key -- owner name per RFC 7929's own hashed-local-part convention.",
            Id: RecordId(26),
            CreatedAtUtc: CreatedAtUtc
        ),
        new(
            Key: "IPSECKEY",
            Name: "vpn.example.com",
            Type: DnsResourceRecordTypeEnum.IpSecKey,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    precedence = 10,
                    // Gateway type 2 (an IPv6 address); algorithm 2 (RSA).
                    gatewayType = 2,
                    algorithm = 2,
                    gatewayAddress = "2001:db8::5",
                    gatewayName = (string)null,
                    publicKey = Convert.FromHexString(
                        "72444864dcee6f72b8615ebda617e31dcc042ee75d010f8b7b69beaf9c350c0c"
                    ),
                }
            ),
            Comments: "The IPsec gateway a client should reach vpn.example.com through.",
            Id: RecordId(27),
            CreatedAtUtc: CreatedAtUtc
        ),
        new(
            Key: "CERT",
            Name: "example.com",
            Type: DnsResourceRecordTypeEnum.Cert,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    // Certificate type 1 (PKIX, an X.509 certificate) in IANA's "Certificate Type
                    // Values" registry.
                    certificateType = 1,
                    keyTag = 54915,
                    algorithm = DnssecAlgorithmEnum.EcdsaP256Sha256,
                    certificate = Convert.FromHexString(
                        "03a35f7919df910420b1439baafe3555e09c9d7630dbae339fc394fdc461989f"
                    ),
                }
            ),
            Comments: "An X.509 certificate published directly in DNS for this domain.",
            Id: RecordId(28),
            CreatedAtUtc: CreatedAtUtc
        ),
        new(
            Key: "DS",
            Name: "child.example.com",
            Type: DnsResourceRecordTypeEnum.DS,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    keyTag = 54915,
                    algorithm = DnssecAlgorithmEnum.EcdsaP256Sha256,
                    // Digest type 2 (SHA-256, RFC 4509): a 32-byte SHA-256 digest standing in for
                    // the child zone's DNSKEY digest.
                    digestType = 2,
                    digest = Convert.FromHexString(
                        "124cc790550bb826f66f437fa675318e67ec346f63e0adc89fd2b187053f2418"
                    ),
                }
            ),
            Comments: "Published by the parent zone (example.com) to vouch for child.example.com's own DNSKEY, forming the chain of trust.",
            Id: RecordId(29),
            CreatedAtUtc: CreatedAtUtc
        ),
        new(
            Key: "CDS",
            Name: "child.example.com",
            Type: DnsResourceRecordTypeEnum.Cds,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    keyTag = 54915,
                    algorithm = DnssecAlgorithmEnum.EcdsaP256Sha256,
                    digestType = 2,
                    digest = Convert.FromHexString(
                        "124cc790550bb826f66f437fa675318e67ec346f63e0adc89fd2b187053f2418"
                    ),
                }
            ),
            Comments: "Published by the child zone itself, at its own apex, so the parent can pick this up automatically instead of requiring a manual DS update.",
            Id: RecordId(30),
            CreatedAtUtc: CreatedAtUtc
        ),
        new(
            Key: "DNSKEY",
            Name: "example.com",
            Type: DnsResourceRecordTypeEnum.DnsKey,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    isZoneKey = true,
                    isSecureEntryPoint = true,
                    algorithm = DnssecAlgorithmEnum.EcdsaP256Sha256,
                    // A raw 64-byte ECDSA P-256 public key (32-byte X then 32-byte Y), as RFC 6605
                    // encodes it.
                    publicKey = Convert.FromHexString(
                        "1e2bb7f00a7dfb0b1a5eccfc6ef152ff325ade02b9699bb818c64cf4ba5bd14afe66e0da6cbe16261cf87cf2395f41e35011d06b9c33e7d40216ac36682588d1"
                    ),
                    isRevoked = false,
                }
            ),
            Comments: "This zone's own key-signing key, published at its apex.",
            Id: RecordId(31),
            CreatedAtUtc: CreatedAtUtc
        ),
        new(
            Key: "CDNSKEY",
            Name: "example.com",
            Type: DnsResourceRecordTypeEnum.CdnsKey,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    isZoneKey = true,
                    isSecureEntryPoint = true,
                    algorithm = DnssecAlgorithmEnum.EcdsaP256Sha256,
                    publicKey = Convert.FromHexString(
                        "1e2bb7f00a7dfb0b1a5eccfc6ef152ff325ade02b9699bb818c64cf4ba5bd14afe66e0da6cbe16261cf87cf2395f41e35011d06b9c33e7d40216ac36682588d1"
                    ),
                    isRevoked = false,
                }
            ),
            Comments: "This zone's own copy of the DNSKEY it wants a parent zone to publish as a DS record automatically.",
            Id: RecordId(32),
            CreatedAtUtc: CreatedAtUtc
        ),
        new(
            Key: "RRSIG",
            Name: "www.example.com",
            Type: DnsResourceRecordTypeEnum.RrSig,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    typeCovered = DnsResourceRecordTypeEnum.A,
                    algorithm = DnssecAlgorithmEnum.EcdsaP256Sha256,
                    labels = 2,
                    originalTimeToLiveSeconds = 3600u,
                    signatureExpiration = 1_772_553_600u,
                    signatureInception = 1_769_961_600u,
                    keyTag = 54915,
                    signerName = "example.com",
                    // A raw 64-byte ECDSA P-256 signature (32-byte r then 32-byte s), as RFC 6605
                    // encodes it.
                    signature = Convert.FromHexString(
                        "447811551c443070ebda3ecd76b6a88b08065cee5083ea9b31f29eaa0032ec2fbf5cbe2ecb08a1c2e1af303ebf1fcd30dee91bd15dd0175e9ab904ce985883e3"
                    ),
                }
            ),
            Comments: "The signature over www.example.com's own A record set.",
            Id: RecordId(33),
            CreatedAtUtc: CreatedAtUtc,
            DnsNameFields: ["signerName"]
        ),
        new(
            Key: "NSEC",
            Name: "www.example.com",
            Type: DnsResourceRecordTypeEnum.Nsec,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    nextDomainName = "example.com",
                    types = new[]
                    {
                        DnsResourceRecordTypeEnum.A,
                        DnsResourceRecordTypeEnum.Aaaa,
                        DnsResourceRecordTypeEnum.RrSig,
                        DnsResourceRecordTypeEnum.Nsec,
                    },
                }
            ),
            Comments: "Proves www.example.com is the last name before wrapping back to the zone apex, and lists every type present here.",
            Id: RecordId(34),
            CreatedAtUtc: CreatedAtUtc,
            DnsNameFields: ["nextDomainName"]
        ),
        new(
            Key: "NSEC3",
            Name: "q0g6v9v99vbfr4b9tcqi5j5rncdt9qsu.example.com",
            Type: DnsResourceRecordTypeEnum.Nsec3,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    // Hash algorithm 1 (SHA-1, the only one assigned), 0 iterations and no
                    // salt, the zone's NSEC3 parameters in ZoneDetailExample (RFC 9276 §3.1). The
                    // hashes below stand in for real ones.
                    hashAlgorithm = 1,
                    isOptOut = false,
                    iterations = 0,
                    salt = Array.Empty<byte>(),
                    // A 20-byte SHA-1 digest standing in for the hash of the next owner name in
                    // hash order.
                    nextHashedOwnerName = Convert.FromHexString(
                        "bfdf73d1b75b7d656abd5ff6835595bc58eb8f10"
                    ),
                    types = new[]
                    {
                        DnsResourceRecordTypeEnum.A,
                        DnsResourceRecordTypeEnum.Aaaa,
                        DnsResourceRecordTypeEnum.RrSig,
                    },
                }
            ),
            Comments: "The hashed-owner-name analogue of the NSEC example above, for a zone using NSEC3 instead.",
            Id: RecordId(35),
            CreatedAtUtc: CreatedAtUtc
        ),
        new(
            Key: "NSEC3PARAM",
            Name: "example.com",
            Type: DnsResourceRecordTypeEnum.Nsec3Param,
            TimeToLiveSeconds: 3600,
            Data: Data(
                new
                {
                    hashAlgorithm = 1,
                    iterations = 0,
                    salt = Array.Empty<byte>(),
                }
            ),
            Comments: "Published at this zone's own apex so a resolver knows how to compute the matching NSEC3 hash itself.",
            Id: RecordId(36),
            CreatedAtUtc: CreatedAtUtc
        ),
    ];

    /// <summary>
    ///     This method builds a stable, obviously placeholder identifier for the
    ///     <paramref name="index" />-th example record.
    /// </summary>
    private static Guid RecordId(int index) => Guid.Parse($"00000000-0000-4000-8000-{index:D12}");
}
