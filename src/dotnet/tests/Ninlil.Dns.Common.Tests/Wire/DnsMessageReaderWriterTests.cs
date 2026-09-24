using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Common.Tests.Wire;

/// <summary>
///     This class tests <see cref="DnsMessageReader" />/<see cref="DnsMessageWriter" />.
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
    ///     Regression test for a real bug found while building <c>Protocol.Plain</c>'s smoke
    ///     test: a genuine 3-byte UDP datagram (too short to even contain a complete 12-byte
    ///     header) made <see cref="DnsMessageReader.Read(System.ReadOnlySpan{byte},out int)" />
    ///     throw a raw <see cref="IndexOutOfRangeException" />/<see cref="ArgumentOutOfRangeException" />
    ///     instead of the documented <see cref="FormatException" /> every other malformed-input
    ///     case here already throws -- which <c>Ninlil.Dns.Server.Core.Pipeline.DnsQueryProcessingPipeline</c>
    ///     specifically catches to discard a malformed message silently, per RFC 1035.
    /// </summary>
    [Theory]
    [InlineData(new byte[] { 0xFF, 0x00, 0x11 })]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x00 })]
    public void ATooShortBufferThrowsFormatExceptionNotARawRangeException(byte[] tooShortBuffer)
    {
        Assert.Throws<FormatException>(() => DnsMessageReader.Read(tooShortBuffer));
    }
}
