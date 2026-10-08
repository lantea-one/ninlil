using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Common.Tests.Wire;

/// <summary>
///     This class tests <see cref="DnsMessageReader" /> and <see cref="DnsMessageWriter" /> on
///     in-memory byte arrays: a round trip of an ordinary query, and malformed input.
/// </summary>
public class DnsMessageReaderWriterTests
{
    [Fact]
    public void AnOrdinaryMessageRoundTripsThroughWriteAndRead()
    {
        DnsMessage message = new()
        {
            Id = 1234,
            RecursionDesired = true,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("www.example.com"),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };

        byte[] wire = DnsMessageWriter.Write(message);
        DnsMessage decoded = DnsMessageReader.Read(wire);

        Assert.Equal((ushort)1234, decoded.Id);
        Assert.True(decoded.RecursionDesired);
        Assert.Equal(DnsName.Parse("www.example.com"), decoded.Questions[0].Name);
    }

    /// <summary>
    ///     This test checks that a buffer shorter than the 12-byte header makes
    ///     <see cref="DnsMessageReader.Read(System.ReadOnlySpan{byte},out int)" /> throw
    ///     <see cref="FormatException" />, not a raw <see cref="IndexOutOfRangeException" /> or
    ///     <see cref="ArgumentOutOfRangeException" />.
    ///     <c>Ninlil.Dns.Server.Core.Pipeline.DnsQueryProcessingPipeline</c> drops a malformed
    ///     message silently only when it sees <see cref="FormatException" />.
    /// </summary>
    [Theory]
    [InlineData(new byte[] { 0xFF, 0x00, 0x11 })]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x00 })]
    public void ATooShortBufferThrowsFormatExceptionNotARawRangeException(byte[] tooShortBuffer)
    {
        Assert.Throws<FormatException>(() => DnsMessageReader.Read(tooShortBuffer));
    }

    /// <summary>
    ///     This method builds a message header with the given section counts (ID 1, a query).
    /// </summary>
    private static List<byte> Header(int questions, int answers, int additional)
    {
        List<byte> bytes = [0, 1, 0, 0];
        foreach (int count in new[] { questions, answers, 0, additional })
        {
            bytes.Add((byte)(count >> 8));
            bytes.Add((byte)count);
        }

        return bytes;
    }

    /// <summary>
    ///     This method encodes a name given as label lengths, using pointer-free wire form.
    /// </summary>
    private static byte[] RawName(params int[] labelLengths)
    {
        List<byte> bytes = [];
        foreach (int length in labelLengths)
        {
            bytes.Add((byte)length);
            bytes.AddRange(Enumerable.Repeat((byte)'a', length));
        }

        bytes.Add(0);
        return [.. bytes];
    }

    /// <summary>
    ///     This method encodes one resource record with the given raw owner name bytes.
    /// </summary>
    private static byte[] Record(
        byte[] owner,
        ushort type,
        ushort recordClass,
        uint ttl,
        byte[] rdata
    )
    {
        List<byte> bytes = [.. owner, (byte)(type >> 8), (byte)type];
        bytes.AddRange([(byte)(recordClass >> 8), (byte)recordClass]);
        bytes.AddRange([(byte)(ttl >> 24), (byte)(ttl >> 16), (byte)(ttl >> 8), (byte)ttl]);
        bytes.AddRange([(byte)(rdata.Length >> 8), (byte)rdata.Length]);
        bytes.AddRange(rdata);
        return [.. bytes];
    }

    /// <summary>
    ///     This method encodes a minimal TSIG record (HMAC-SHA256, empty MAC) with the given
    ///     CLASS and TTL.
    /// </summary>
    private static byte[] TsigRecord(ushort recordClass = 255, uint ttl = 0)
    {
        List<byte> rdata = [.. RawName(11), .. new byte[6], 1, 0x2C, 0, 0, 0, 1, 0, 0, 0, 0];
        return Record(RawName(3), 250, recordClass, ttl, [.. rdata]);
    }

    private static byte[] OptRecord() => Record([0], 41, 1232, 0, []);

    private static byte[] ARecord() => Record(RawName(1), 1, 1, 60, [1, 2, 3, 4]);

    private static byte[] Message(int questions, int answers, params byte[][] additional)
    {
        List<byte> bytes = Header(questions, answers, additional.Length);
        foreach (byte[] record in additional)
            bytes.AddRange(record);
        return [.. bytes];
    }

    private static byte[] QuestionWithName(byte[] name)
    {
        List<byte> bytes = Header(1, 0, 0);
        bytes.AddRange(name);
        bytes.AddRange([0, 1, 0, 1]);
        return [.. bytes];
    }

    [Fact]
    public void ANameOf255OctetsIsAccepted()
    {
        // 63 + 63 + 63 + 61 label octets: wire length 1 + 4 + 250 = 255 with the root octet.
        DnsMessage message = DnsMessageReader.Read(QuestionWithName(RawName(63, 63, 63, 61)));

        Assert.Equal(255, message.Questions[0].Name.WireLength);
    }

    [Fact]
    public void ANameOf256OctetsThrowsFormatException()
    {
        Assert.Throws<FormatException>(() =>
            DnsMessageReader.Read(QuestionWithName(RawName(63, 63, 63, 62)))
        );
    }

    [Fact]
    public void AVeryLongPointerFreeNameThrowsFormatException()
    {
        // 4 000 one-octet labels.
        Assert.Throws<FormatException>(() =>
            DnsMessageReader.Read(QuestionWithName(RawName([.. Enumerable.Repeat(1, 4000)])))
        );
    }

    [Fact]
    public void ATsigAlgorithmNameThatIsACompressionPointerThrowsFormatException()
    {
        // The algorithm name is the two-byte pointer 0xC00C, which the uncompressed codec rejects.
        List<byte> rdata = [0xC0, 0x0C, .. new byte[6], 1, 0x2C, 0, 0, 0, 1, 0, 0, 0, 0];
        byte[] tsig = Record(RawName(3), 250, 255, 0, [.. rdata]);

        Assert.Throws<FormatException>(() => DnsMessageReader.Read(Message(0, 0, tsig)));
    }

    [Fact]
    public void ANaptrRecordWithATruncatedCharacterStringThrowsFormatException()
    {
        // Order, preference, then a flags string that claims 9 octets but has 1.
        byte[] naptr = Record(RawName(1), 35, 1, 60, [0, 1, 0, 1, 9, (byte)'x']);
        List<byte> bytes = Header(0, 1, 0);
        bytes.AddRange(naptr);

        Assert.Throws<FormatException>(() => DnsMessageReader.Read([.. bytes]));
    }

    [Fact]
    public void AnIpSecKeyRecordWithATruncatedGatewayThrowsFormatException()
    {
        // Gateway type 1 needs a four-octet address but the RDATA has two.
        byte[] ipsec = Record(RawName(1), 45, 1, 60, [10, 1, 2, 1, 2]);
        List<byte> bytes = Header(0, 1, 0);
        bytes.AddRange(ipsec);

        Assert.Throws<FormatException>(() => DnsMessageReader.Read([.. bytes]));
    }

    [Fact]
    public void ARecordAfterTheTsigThrowsFormatException()
    {
        Assert.Throws<FormatException>(() =>
            DnsMessageReader.Read(Message(0, 0, TsigRecord(), ARecord()))
        );
    }

    [Fact]
    public void TwoTsigRecordsThrowFormatException()
    {
        Assert.Throws<FormatException>(() =>
            DnsMessageReader.Read(Message(0, 0, TsigRecord(), TsigRecord()))
        );
    }

    [Fact]
    public void TwoOptRecordsThrowFormatException()
    {
        Assert.Throws<FormatException>(() =>
            DnsMessageReader.Read(Message(0, 0, OptRecord(), OptRecord()))
        );
    }

    [Theory]
    [InlineData((ushort)1, 0u)]
    [InlineData((ushort)255, 1u)]
    public void ATsigWithAWrongClassOrTtlThrowsFormatException(ushort recordClass, uint ttl)
    {
        Assert.Throws<FormatException>(() =>
            DnsMessageReader.Read(Message(0, 0, TsigRecord(recordClass, ttl)))
        );
    }

    [Fact]
    public void AValidSignedMessageStillReportsItsTsigOffset()
    {
        byte[] opt = OptRecord();
        byte[] a = ARecord();
        byte[] wire = Message(0, 0, a, opt, TsigRecord());

        DnsMessage message = DnsMessageReader.Read(wire, out int tsigOffset);

        Assert.NotNull(message.Tsig);
        Assert.NotNull(message.Edns);
        Assert.Single(message.AdditionalRecords);
        Assert.Equal(12 + a.Length + opt.Length, tsigOffset);
    }
}
