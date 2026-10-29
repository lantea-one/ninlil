using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Common.Tests.Wire;

/// <summary>
///     This class builds the corpus of messages that <see cref="DnsMessageWriterGoldenTests" />
///     writes and compares with the bytes recorded in <see cref="DnsMessageGoldenExpected" />. The
///     recorded bytes were captured from the writer before its compression map stopped building
///     strings, so the corpus pins the writer's output byte for byte. A message is built in code
///     (not stored as bytes) so the test can also decode and rewrite it.
/// </summary>
internal static class DnsMessageGoldenCorpus
{
    /// <summary>
    ///     This property contains every corpus message by name, in a stable order.
    /// </summary>
    public static IReadOnlyList<(string Name, Func<DnsMessage> Build)> All { get; } =
    [
        ("query-plain", () => Query("www.example.com", DnsResourceRecordTypeEnum.A)),
        (
            "query-edns-do",
            () => WithEdns(Query("www.example.com", DnsResourceRecordTypeEnum.Aaaa), true)
        ),
        (
            "query-edns-cookie",
            () =>
                WithEdns(
                    Query("example.org", DnsResourceRecordTypeEnum.MX),
                    false,
                    new DnsEdnsOption(10, [1, 2, 3, 4, 5, 6, 7, 8])
                )
        ),
        (
            "query-edns-subnet-and-padding",
            () =>
                WithEdns(
                    Query("a.b.c.d.example.net", DnsResourceRecordTypeEnum.Txt),
                    true,
                    new DnsEdnsOption(8, [0, 1, 24, 0, 192, 0, 2]),
                    new DnsEdnsOption(12, new byte[40])
                )
        ),
        (
            "query-edns-empty-options",
            () => WithEdns(Query("example.com", DnsResourceRecordTypeEnum.NS), false)
        ),
        (
            "query-edns-zero-length-option",
            () =>
                WithEdns(
                    Query("example.com", DnsResourceRecordTypeEnum.NS),
                    false,
                    new DnsEdnsOption(4, []),
                    new DnsEdnsOption(65001, [9])
                )
        ),
        (
            "query-tsig",
            () =>
            {
                DnsMessage message = Query("zone.example.com", DnsResourceRecordTypeEnum.Soa);
                message.Tsig = Tsig("key.example.com");
                return message;
            }
        ),
        (
            "query-edns-and-tsig",
            () =>
            {
                DnsMessage message = WithEdns(
                    Query("zone.example.com", DnsResourceRecordTypeEnum.Soa),
                    true,
                    new DnsEdnsOption(10, [8, 7, 6, 5, 4, 3, 2, 1])
                );
                message.Tsig = Tsig("zone.example.com");
                return message;
            }
        ),
        (
            "response-tsig-badtime-other-data",
            () =>
            {
                DnsMessage message = Response("www.example.com", DnsResourceRecordTypeEnum.A);
                message.ResponseCode = DnsResponseCodeEnum.NotAuthoritative;
                message.Tsig = new DnsTsigOptions(
                    DnsName.Parse("WWW.Example.COM"),
                    DnsTsigOptions.HmacSha512AlgorithmName,
                    0x0000_1234_5678_9ABCUL,
                    300,
                    new byte[64],
                    7,
                    DnsResponseCodeEnum.BadTime,
                    [0, 0, 0, 0, 0x12, 0x34]
                );
                return message;
            }
        ),
        (
            "response-answer-owner-is-question",
            () =>
            {
                DnsMessage message = Response("www.example.com", DnsResourceRecordTypeEnum.A);
                message.Answers.Add(A("www.example.com", 300, 192, 0, 2, 1));
                return message;
            }
        ),
        (
            "response-five-answers-same-owner",
            () =>
            {
                DnsMessage message = Response("www.example.com", DnsResourceRecordTypeEnum.A);
                for (byte i = 1; i <= 5; i++)
                    message.Answers.Add(A("www.example.com", 60u * i, 192, 0, 2, i));
                return message;
            }
        ),
        (
            "response-five-answers-different-owners",
            () =>
            {
                DnsMessage message = Response("www.example.com", DnsResourceRecordTypeEnum.A);
                message.Answers.Add(A("www.example.com", 300, 192, 0, 2, 1));
                message.Answers.Add(A("mail.example.com", 300, 192, 0, 2, 2));
                message.Answers.Add(A("ftp.www.example.com", 300, 192, 0, 2, 3));
                message.Answers.Add(A("example.com", 300, 192, 0, 2, 4));
                message.Answers.Add(A("www.example.org", 300, 192, 0, 2, 5));
                return message;
            }
        ),
        (
            "response-cname-chain",
            () =>
            {
                DnsMessage message = Response("www.example.com", DnsResourceRecordTypeEnum.A);
                message.Answers.Add(
                    NameRecord(
                        "www.example.com",
                        DnsResourceRecordTypeEnum.Cname,
                        "web.edge.example.com"
                    )
                );
                message.Answers.Add(
                    NameRecord(
                        "web.edge.example.com",
                        DnsResourceRecordTypeEnum.Cname,
                        "cdn.example.net"
                    )
                );
                message.Answers.Add(A("cdn.example.net", 30, 198, 51, 100, 7));
                return message;
            }
        ),
        (
            "response-authority-ns-and-additional-glue",
            () =>
            {
                DnsMessage message = Response("host.sub.example.com", DnsResourceRecordTypeEnum.A);
                message.AuthorityRecords.Add(
                    NameRecord(
                        "sub.example.com",
                        DnsResourceRecordTypeEnum.NS,
                        "ns1.sub.example.com"
                    )
                );
                message.AuthorityRecords.Add(
                    NameRecord(
                        "sub.example.com",
                        DnsResourceRecordTypeEnum.NS,
                        "ns2.sub.example.com"
                    )
                );
                message.AdditionalRecords.Add(A("ns1.sub.example.com", 3600, 203, 0, 113, 1));
                message.AdditionalRecords.Add(A("ns2.sub.example.com", 3600, 203, 0, 113, 2));
                message.AdditionalRecords.Add(Aaaa("ns2.sub.example.com", 3600));
                return message;
            }
        ),
        (
            "response-mixed-case-question-repeats-in-answer",
            () =>
            {
                DnsMessage message = Response("WWW.Example.COM", DnsResourceRecordTypeEnum.A);
                message.Answers.Add(A("www.example.com", 300, 192, 0, 2, 1));
                message.Answers.Add(A("Www.EXAMPLE.com", 300, 192, 0, 2, 2));
                return message;
            }
        ),
        (
            "response-mixed-case-suffix-shared-by-other-names",
            () =>
            {
                DnsMessage message = Response("Mail.EXAMPLE.com", DnsResourceRecordTypeEnum.A);
                message.Answers.Add(A("other.example.COM", 300, 192, 0, 2, 1));
                message.Answers.Add(A("deep.Other.Example.com", 300, 192, 0, 2, 2));
                message.AuthorityRecords.Add(
                    NameRecord("EXAMPLE.COM", DnsResourceRecordTypeEnum.NS, "ns.example.com")
                );
                return message;
            }
        ),
        (
            "response-nxdomain-soa-and-edns",
            () =>
            {
                DnsMessage message = WithEdns(
                    Response("nope.example.com", DnsResourceRecordTypeEnum.A),
                    true
                );
                message.ResponseCode = DnsResponseCodeEnum.NxDomain;
                message.IsAuthoritativeAnswer = true;
                message.AuthorityRecords.Add(
                    Soa("example.com", "ns1.example.com", "hostmaster.example.com")
                );
                return message;
            }
        ),
        (
            "response-extended-response-code-with-edns",
            () =>
            {
                DnsMessage message = WithEdns(
                    Response("www.example.com", DnsResourceRecordTypeEnum.A),
                    false
                );
                message.ResponseCode = DnsResponseCodeEnum.BadCookie;
                return message;
            }
        ),
        (
            "response-every-flag-set",
            () =>
            {
                DnsMessage message = Response("flags.example.com", DnsResourceRecordTypeEnum.Txt);
                message.IsAuthoritativeAnswer = true;
                message.IsTruncated = true;
                message.RecursionDesired = true;
                message.RecursionAvailable = true;
                message.AuthenticData = true;
                message.CheckingDisabled = true;
                message.ResponseCode = DnsResponseCodeEnum.Refused;
                message.Answers.Add(Txt("flags.example.com", "hello", "world"));
                return message;
            }
        ),
        (
            "response-dnssec-rrsig-nsec-in-authority",
            () =>
            {
                DnsMessage message = WithEdns(
                    Response("secure.example.com", DnsResourceRecordTypeEnum.A),
                    true
                );
                message.AuthenticData = true;
                message.Answers.Add(A("secure.example.com", 300, 192, 0, 2, 9));
                message.Answers.Add(
                    Raw(
                        "secure.example.com",
                        DnsResourceRecordTypeEnum.RrSig,
                        300,
                        RrSigData(1, "example.com", 3)
                    )
                );
                message.AuthorityRecords.Add(
                    Raw(
                        "secure.example.com",
                        DnsResourceRecordTypeEnum.Nsec,
                        300,
                        [
                            .. DnsName.Parse("z.example.com").ToUncompressedWireBytes(),
                            0,
                            2,
                            0x40,
                            0x01,
                        ]
                    )
                );
                message.AuthorityRecords.Add(
                    Raw(
                        "example.com",
                        DnsResourceRecordTypeEnum.RrSig,
                        300,
                        RrSigData(6, "example.com", 7)
                    )
                );
                return message;
            }
        ),
        (
            "response-mx-and-srv-owners",
            () =>
            {
                DnsMessage message = Response(
                    "_sip._tcp.example.com",
                    DnsResourceRecordTypeEnum.Srv
                );
                message.Answers.Add(
                    Raw(
                        "_sip._tcp.example.com",
                        DnsResourceRecordTypeEnum.Srv,
                        120,
                        [
                            0,
                            1,
                            0,
                            2,
                            0x13,
                            0xC4,
                            .. DnsName.Parse("sip.example.com").ToUncompressedWireBytes(),
                        ]
                    )
                );
                message.AdditionalRecords.Add(A("sip.example.com", 120, 192, 0, 2, 66));
                message.AdditionalRecords.Add(
                    Raw(
                        "example.com",
                        DnsResourceRecordTypeEnum.MX,
                        120,
                        [0, 10, .. DnsName.Parse("mx.example.com").ToUncompressedWireBytes()]
                    )
                );
                return message;
            }
        ),
        (
            "two-questions-sharing-a-suffix",
            () =>
            {
                DnsMessage message = Query("one.example.com", DnsResourceRecordTypeEnum.A);
                message.Questions.Add(
                    new DnsQuestion(
                        DnsName.Parse("two.example.com"),
                        DnsResourceRecordTypeEnum.Aaaa,
                        DnsClassEnum.Internet
                    )
                );
                message.Questions.Add(
                    new DnsQuestion(
                        DnsName.Parse("one.example.com"),
                        DnsResourceRecordTypeEnum.Txt,
                        DnsClassEnum.Chaos
                    )
                );
                return message;
            }
        ),
        ("root-name-question", () => Query(".", DnsResourceRecordTypeEnum.NS)),
        (
            "root-name-owner-and-edns",
            () =>
            {
                DnsMessage message = WithEdns(Response(".", DnsResourceRecordTypeEnum.NS), true);
                message.Answers.Add(
                    NameRecord(".", DnsResourceRecordTypeEnum.NS, "a.root-servers.net")
                );
                message.AdditionalRecords.Add(A("a.root-servers.net", 518400, 198, 41, 0, 4));
                message.AdditionalRecords.Add(A("b.root-servers.net", 518400, 170, 247, 170, 2));
                return message;
            }
        ),
        (
            "name-with-escaped-dot-label",
            () =>
            {
                DnsMessage message = Response(@"a\.b.example.com", DnsResourceRecordTypeEnum.A);
                message.Answers.Add(A(@"a\.b.example.com", 300, 192, 0, 2, 1));
                message.Answers.Add(A("b.example.com", 300, 192, 0, 2, 2));
                message.Answers.Add(A("a.b.example.com", 300, 192, 0, 2, 3));
                return message;
            }
        ),
        (
            "name-with-high-bytes",
            () =>
            {
                DnsMessage message = Response(
                    @"\200\255\127.example.com",
                    DnsResourceRecordTypeEnum.A
                );
                message.Answers.Add(A(@"\200\255\127.example.com", 300, 192, 0, 2, 1));
                message.Answers.Add(A(@"x.\200\255\127.example.com", 300, 192, 0, 2, 2));
                message.Answers.Add(A(@"\200\255\126.example.com", 300, 192, 0, 2, 3));
                return message;
            }
        ),
        (
            "labels-that-differ-only-in-where-they-split",
            () =>
            {
                DnsMessage message = Response("ab.c.example.com", DnsResourceRecordTypeEnum.A);
                message.Answers.Add(A("a.bc.example.com", 300, 192, 0, 2, 1));
                message.Answers.Add(A("abc.example.com", 300, 192, 0, 2, 2));
                message.Answers.Add(A("c.example.com", 300, 192, 0, 2, 3));
                message.Answers.Add(A("bc.example.com", 300, 192, 0, 2, 4));
                return message;
            }
        ),
        (
            "bytes-next-to-the-letter-ranges-are-not-folded",
            () =>
            {
                // 0x40 and 0x60 sit just outside A-Z and a-z, 0x5B and 0x7B just after them. Only
                // letters compare case-insensitively, so none of these names may point to another.
                DnsMessage message = Response(@"x[y.example.com", DnsResourceRecordTypeEnum.A);
                message.Answers.Add(A(@"X{Y.example.com", 300, 192, 0, 2, 1));
                message.Answers.Add(A(@"x@y.example.com", 300, 192, 0, 2, 2));
                message.Answers.Add(A(@"X`Y.example.com", 300, 192, 0, 2, 3));
                message.Answers.Add(A(@"X[Y.EXAMPLE.com", 300, 192, 0, 2, 4));
                return message;
            }
        ),
        (
            "longest-name-of-single-letter-labels",
            () =>
            {
                string name = string.Join('.', Enumerable.Repeat("a", 127));
                DnsMessage message = Response(name, DnsResourceRecordTypeEnum.A);
                message.Answers.Add(A(name, 300, 192, 0, 2, 1));
                message.Answers.Add(
                    A(string.Join('.', Enumerable.Repeat("A", 60)), 300, 192, 0, 2, 2)
                );
                message.Answers.Add(
                    A(string.Join('.', Enumerable.Repeat("a", 61)), 300, 192, 0, 2, 3)
                );
                return message;
            }
        ),
        (
            "labels-of-63-octets",
            () =>
            {
                string label = new('l', 63);
                DnsMessage message = Response(
                    $"{label}.{label}.example",
                    DnsResourceRecordTypeEnum.A
                );
                message.Answers.Add(A($"{label}.{label}.example", 300, 192, 0, 2, 1));
                message.Answers.Add(A($"x.{label}.example", 300, 192, 0, 2, 2));
                message.Answers.Add(A($"{label.ToUpperInvariant()}.example", 300, 192, 0, 2, 3));
                return message;
            }
        ),
        (
            "update-with-zone-authority-and-additional",
            () =>
            {
                DnsMessage message = Query("example.com", DnsResourceRecordTypeEnum.Soa);
                message.Opcode = DnsOpcodeEnum.Update;
                message.RecursionDesired = false;
                message.AuthorityRecords.Add(A("host.example.com", 300, 192, 0, 2, 10));
                message.AuthorityRecords.Add(
                    Raw("old.example.com", DnsResourceRecordTypeEnum.A, 0, [])
                );
                message.AdditionalRecords.Add(A("host.example.com", 300, 192, 0, 2, 11));
                return message;
            }
        ),
        (
            "notify-with-soa-answer",
            () =>
            {
                DnsMessage message = Query("example.com", DnsResourceRecordTypeEnum.Soa);
                message.Opcode = DnsOpcodeEnum.Notify;
                message.IsAuthoritativeAnswer = true;
                message.Answers.Add(
                    Soa("example.com", "ns1.example.com", "hostmaster.example.com")
                );
                return message;
            }
        ),
        ("empty-message", () => new DnsMessage { Id = 0xFFFF }),
        (
            "tsig-key-name-equals-owner-stays-uncompressed",
            () =>
            {
                DnsMessage message = Response("key.example.com", DnsResourceRecordTypeEnum.A);
                message.Answers.Add(A("key.example.com", 300, 192, 0, 2, 1));
                message.Tsig = Tsig("key.example.com");
                return message;
            }
        ),
        ("large-message-names-past-the-pointer-limit", () => LargeMessage()),
    ];

    /// <summary>
    ///     This method builds a query with one question and Recursion Desired set.
    /// </summary>
    private static DnsMessage Query(string name, DnsResourceRecordTypeEnum type)
    {
        DnsMessage message = new() { Id = 0x1A2B, RecursionDesired = true };
        message.Questions.Add(new DnsQuestion(DnsName.Parse(name), type, DnsClassEnum.Internet));
        return message;
    }

    /// <summary>
    ///     This method builds a response with one question and no records yet.
    /// </summary>
    private static DnsMessage Response(string name, DnsResourceRecordTypeEnum type)
    {
        DnsMessage message = Query(name, type);
        message.IsResponse = true;
        message.RecursionAvailable = true;
        return message;
    }

    /// <summary>
    ///     This method adds an EDNS pseudo-record to <paramref name="message" />.
    /// </summary>
    private static DnsMessage WithEdns(
        DnsMessage message,
        bool dnssecOk,
        params DnsEdnsOption[] options
    )
    {
        message.Edns = new DnsEdnsOptions(0, 1232, dnssecOk, options);
        return message;
    }

    /// <summary>
    ///     This method builds a TSIG pseudo-record signed with a key of the given name.
    /// </summary>
    private static DnsTsigOptions Tsig(string keyName) =>
        new(
            DnsName.Parse(keyName),
            DnsTsigOptions.HmacSha256AlgorithmName,
            1_700_000_000UL,
            300,
            Pattern(32, 11),
            0x1A2B,
            DnsResponseCodeEnum.NoError,
            []
        );

    /// <summary>
    ///     This method builds an IN A record.
    /// </summary>
    private static DnsResourceRecord A(
        string owner,
        uint timeToLive,
        byte a,
        byte b,
        byte c,
        byte d
    ) => Raw(owner, DnsResourceRecordTypeEnum.A, timeToLive, [a, b, c, d]);

    /// <summary>
    ///     This method builds an IN AAAA record for 2001:db8::1.
    /// </summary>
    private static DnsResourceRecord Aaaa(string owner, uint timeToLive) =>
        Raw(
            owner,
            DnsResourceRecordTypeEnum.Aaaa,
            timeToLive,
            [0x20, 0x01, 0x0D, 0xB8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1]
        );

    /// <summary>
    ///     This method builds an IN TXT record with one character-string per argument.
    /// </summary>
    private static DnsResourceRecord Txt(string owner, params string[] strings)
    {
        List<byte> data = [];
        foreach (string text in strings)
        {
            data.Add((byte)text.Length);
            data.AddRange(System.Text.Encoding.ASCII.GetBytes(text));
        }

        return Raw(owner, DnsResourceRecordTypeEnum.Txt, 60, [.. data]);
    }

    /// <summary>
    ///     This method builds a record whose RDATA is one uncompressed name (NS, CNAME, PTR).
    /// </summary>
    private static DnsResourceRecord NameRecord(
        string owner,
        DnsResourceRecordTypeEnum type,
        string target
    ) => Raw(owner, type, 300, DnsName.Parse(target).ToUncompressedWireBytes());

    /// <summary>
    ///     This method builds an SOA record with fixed timers.
    /// </summary>
    private static DnsResourceRecord Soa(string owner, string primary, string mailbox)
    {
        List<byte> data =
        [
            .. DnsName.Parse(primary).ToUncompressedWireBytes(),
            .. DnsName.Parse(mailbox).ToUncompressedWireBytes(),
            .. new byte[]
            {
                0,
                0,
                0,
                1,
                0,
                0,
                0x0E,
                0x10,
                0,
                0,
                0x02,
                0x58,
                0,
                0x12,
                0x75,
                0,
                0,
                0,
                0x01,
                0x2C,
            },
        ];
        return Raw(owner, DnsResourceRecordTypeEnum.Soa, 3600, [.. data]);
    }

    /// <summary>
    ///     This method builds an IN record with opaque RDATA.
    /// </summary>
    private static DnsResourceRecord Raw(
        string owner,
        DnsResourceRecordTypeEnum type,
        uint timeToLive,
        byte[] data
    ) => new(DnsName.Parse(owner), type, DnsClassEnum.Internet, timeToLive, data);

    /// <summary>
    ///     This method builds the RDATA of an RRSIG record covering <paramref name="coveredType" />
    ///     with an uncompressed signer name, so the reader can decode it.
    /// </summary>
    private static byte[] RrSigData(ushort coveredType, string signer, int seed) =>
        [
            (byte)(coveredType >> 8),
            (byte)coveredType,
            13,
            3,
            0,
            0,
            1,
            0x2C,
            0x65,
            0x00,
            0x00,
            0x00,
            0x65,
            0x10,
            0x00,
            0x00,
            0x12,
            0x34,
            .. DnsName.Parse(signer).ToUncompressedWireBytes(),
            .. Pattern(64, seed),
        ];

    /// <summary>
    ///     This method builds <paramref name="length" /> deterministic bytes.
    /// </summary>
    private static byte[] Pattern(int length, int seed)
    {
        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++)
            bytes[i] = (byte)((i * 31) + seed);
        return bytes;
    }

    /// <summary>
    ///     This method builds a message over 16 KiB: 80 TXT records of about 250 octets each with
    ///     distinct owners, so later owners start past offset 0x3FFF and are written but never
    ///     pointed to. It then repeats an early owner (which points back), repeats a late owner
    ///     (written literally again, though its shared suffix points back) and adds an additional
    ///     record and EDNS after the large body.
    /// </summary>
    private static DnsMessage LargeMessage()
    {
        DnsMessage message = Response("n0.big.example.com", DnsResourceRecordTypeEnum.Txt);

        for (int i = 0; i < 80; i++)
            message.Answers.Add(
                Raw(
                    $"n{i}.big.example.com",
                    DnsResourceRecordTypeEnum.Txt,
                    60,
                    [.. new byte[] { 250 }, .. Pattern(250, i)]
                )
            );

        message.Answers.Add(Txt("N0.BIG.example.com", "again-early"));
        message.Answers.Add(Txt("n79.big.example.com", "again-late"));
        message.Answers.Add(Txt("late.big.example.com", "new-late"));
        message.AdditionalRecords.Add(A("n79.BIG.example.com", 60, 192, 0, 2, 79));
        return WithEdns(message, true, new DnsEdnsOption(10, [1, 2, 3, 4, 5, 6, 7, 8]));
    }
}
