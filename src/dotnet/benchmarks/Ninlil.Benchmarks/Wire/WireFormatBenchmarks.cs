using System.Net;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Benchmarks.Wire;

/// <summary>
///     This class measures the DNS wire format in <c>Ninlil.Dns.Common</c> without a database or
///     a socket: <see cref="DnsMessageReader" />, <see cref="DnsMessageWriter" /> and the
///     <see cref="DnsName" /> members the pipeline calls on every query. The messages are a
///     query with an EDNS option and a DNS cookie, and a response with a question, five answers
///     whose names share a suffix (so the writer compresses), an authority record and an RRSIG.
///     The setup checks that each message survives a read and write round trip and that the
///     response is smaller than its uncompressed form would be.
///     <para>
///         The allocation column is the point: the writer and the name members are meant to
///         build no strings for compression, hashing or printing.
///     </para>
/// </summary>
[Config(typeof(StorageConfig))]
public class WireFormatBenchmarks
{
    private DnsMessage _query;
    private DnsMessage _response;
    private byte[] _queryBytes;
    private byte[] _responseBytes;
    private DnsName _name;
    private string _nameText;

    /// <summary>
    ///     This method builds the two messages and checks them.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _name = DnsName.Parse("www.shop.example.com");
        _nameText = "www.shop.example.com";

        _query = new DnsMessage
        {
            Id = 0x1234,
            RecursionDesired = true,
            Questions =
            {
                new DnsQuestion(_name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet),
            },
            Edns = new DnsEdnsOptions(
                0,
                1232,
                true,
                [new DnsEdnsOption((ushort)EdnsOptionCodeEnum.Cookie, new byte[8])]
            ),
        };
        _queryBytes = DnsMessageWriter.Write(_query);

        _response = new DnsMessage
        {
            Id = 0x1234,
            IsResponse = true,
            RecursionDesired = true,
            RecursionAvailable = true,
            Questions =
            {
                new DnsQuestion(_name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet),
            },
            Edns = new DnsEdnsOptions(0, 1232, true, []),
        };

        // Five answers: a CNAME chain and three A records, all under example.com.
        DnsName cdn = DnsName.Parse("edge.cdn.shop.example.com");
        _response.Answers.Add(
            new DnsResourceRecord(
                _name,
                DnsResourceRecordTypeEnum.Cname,
                DnsClassEnum.Internet,
                300,
                new CnameRecordDataCodec().Write(new CnameRecordData(cdn))
            )
        );

        for (int index = 1; index <= 3; index++)
            _response.Answers.Add(
                new DnsResourceRecord(
                    cdn,
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet,
                    300,
                    new ARecordDataCodec().Write(
                        new ARecordData(IPAddress.Parse($"192.0.2.{index}"))
                    )
                )
            );

        _response.Answers.Add(
            new DnsResourceRecord(
                cdn,
                DnsResourceRecordTypeEnum.RrSig,
                DnsClassEnum.Internet,
                300,
                new RrSigRecordDataCodec().Write(
                    new RrSigRecordData(
                        DnsResourceRecordTypeEnum.A,
                        DnssecAlgorithmEnum.EcdsaP256Sha256,
                        5,
                        300,
                        2_000_000_000,
                        1_700_000_000,
                        12345,
                        DnsName.Parse("example.com"),
                        new byte[64]
                    )
                )
            )
        );
        _response.AuthorityRecords.Add(
            new DnsResourceRecord(
                DnsName.Parse("example.com"),
                DnsResourceRecordTypeEnum.NS,
                DnsClassEnum.Internet,
                3600,
                new NsRecordDataCodec().Write(new NsRecordData(DnsName.Parse("ns1.example.com")))
            )
        );
        _responseBytes = DnsMessageWriter.Write(_response);

        Check(_queryBytes, "the query");
        Check(_responseBytes, "the response");

        // Every name written in full plus the fixed parts (header, question type and class,
        // type/class/TTL/length of each record, the OPT record) is the size without
        // compression; the compressed response must be smaller.
        int uncompressed =
            12
            + _name.WireLength
            + 4
            + 11
            + _response
                .Answers.Concat(_response.AuthorityRecords)
                .Sum(record => record.Name.WireLength + 10 + record.Data.Length);
        Check(_responseBytes.Length < uncompressed, "the response is compressed");
    }

    private static void Check(byte[] bytes, string what)
    {
        DnsMessage read = DnsMessageReader.Read(bytes);

        if (!DnsMessageWriter.Write(read).AsSpan().SequenceEqual(bytes))
            throw new InvalidOperationException(
                $"Benchmark setup check failed: {what} does not survive a read and write round trip."
            );
    }

    private static void Check(bool condition, string what)
    {
        if (!condition)
            throw new InvalidOperationException($"Benchmark setup check failed: {what}.");
    }

    /// <summary>Reads the query with its EDNS option and cookie.</summary>
    [Benchmark]
    public DnsMessage ReadQuery() => DnsMessageReader.Read(_queryBytes);

    /// <summary>Reads the response with compressed names and an RRSIG.</summary>
    [Benchmark]
    public DnsMessage ReadResponse() => DnsMessageReader.Read(_responseBytes);

    /// <summary>Writes the query.</summary>
    [Benchmark]
    public byte[] WriteQuery() => DnsMessageWriter.Write(_query);

    /// <summary>Writes the response, compressing the repeated suffixes.</summary>
    [Benchmark]
    public byte[] WriteResponse() => DnsMessageWriter.Write(_response);

    /// <summary>Prints a name.</summary>
    [Benchmark]
    public string NameToString() => _name.ToString();

    /// <summary>Hashes a name.</summary>
    [Benchmark]
    public int NameGetHashCode() => _name.GetHashCode();

    /// <summary>Parses a name from its text.</summary>
    [Benchmark]
    public bool NameTryParse() => DnsName.TryParse(_nameText, out _);
}
