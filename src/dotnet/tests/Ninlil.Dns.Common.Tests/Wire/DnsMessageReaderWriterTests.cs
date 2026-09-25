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
}
