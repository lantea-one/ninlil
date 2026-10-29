using System.Security.Cryptography;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Common.Tests.Wire;

/// <summary>
///     This class pins <see cref="DnsMessageWriter" />'s output byte for byte. It writes every
///     message of <see cref="DnsMessageGoldenCorpus" /> and compares the bytes with the capture in
///     <see cref="DnsMessageGoldenExpected" />, which came from the writer before its compression
///     map was keyed by <see cref="DnsName" /> suffixes instead of built strings. It also decodes
///     each capture and writes the decoded message again, which must give the same bytes. The
///     tests are in-memory checks.
/// </summary>
public class DnsMessageWriterGoldenTests
{
    /// <summary>
    ///     This property lists the corpus message names for the theories.
    /// </summary>
    public static IEnumerable<object[]> MessageNames =>
        DnsMessageGoldenCorpus.All.Select(entry => new object[] { entry.Name });

    /// <summary>
    ///     This test checks that the corpus is large enough to cover the cases the writer must keep
    ///     byte-identical, and that every message has a recorded encoding.
    /// </summary>
    [Fact]
    public void Corpus_HasAtLeastThirtyMessagesAndEachHasARecordedEncoding()
    {
        Assert.True(DnsMessageGoldenCorpus.All.Count >= 30);
        Assert.Equal(
            DnsMessageGoldenCorpus.All.Select(entry => entry.Name).Order(),
            DnsMessageGoldenExpected.ByName.Keys.Order()
        );
        Assert.Equal(
            DnsMessageGoldenCorpus.All.Count,
            DnsMessageGoldenCorpus.All.Select(entry => entry.Name).Distinct().Count()
        );
    }

    /// <summary>
    ///     This test checks that the corpus contains a message longer than the 14-bit compression
    ///     pointer range, so names written past offset 0x3FFF are covered.
    /// </summary>
    [Fact]
    public void Corpus_HasAMessageLongerThanTheCompressionPointerRange()
    {
        Assert.Contains(
            DnsMessageGoldenCorpus.All,
            entry => DnsMessageWriter.Write(entry.Build()).Length > 0x3FFF + 4096
        );
    }

    /// <summary>
    ///     This test checks that writing each corpus message gives exactly the recorded bytes.
    /// </summary>
    [Theory]
    [MemberData(nameof(MessageNames))]
    public void Write_ACorpusMessage_GivesTheRecordedBytes(string name)
    {
        DnsMessage message = Build(name);

        byte[] written = DnsMessageWriter.Write(message);

        Assert.Equal(DnsMessageGoldenExpected.ByName[name], Describe(written));
    }

    /// <summary>
    ///     This test checks that writing a message twice gives the same bytes both times, so the
    ///     writer keeps no state between messages that changes its output.
    /// </summary>
    [Theory]
    [MemberData(nameof(MessageNames))]
    public void Write_ACorpusMessageTwiceInARow_GivesTheSameBytes(string name)
    {
        byte[] first = DnsMessageWriter.Write(Build(name));
        byte[] second = DnsMessageWriter.Write(Build(name));

        Assert.Equal(first, second);
    }

    /// <summary>
    ///     This test checks that writing the corpus messages in an interleaved order gives the
    ///     recorded bytes for each, so a buffer or map reused between calls cannot leak a name from
    ///     one message into the next.
    /// </summary>
    [Fact]
    public void Write_AllCorpusMessagesBackToBackInReverseOrder_GiveTheRecordedBytes()
    {
        foreach ((string name, Func<DnsMessage> build) in DnsMessageGoldenCorpus.All.Reverse())
            Assert.Equal(
                DnsMessageGoldenExpected.ByName[name],
                Describe(DnsMessageWriter.Write(build()))
            );
    }

    /// <summary>
    ///     This test checks that decoding each recorded message and writing it again reproduces the
    ///     same bytes: the writer compresses what the reader decompressed in the same way.
    /// </summary>
    [Theory]
    [MemberData(nameof(MessageNames))]
    public void ReadThenWrite_ACorpusMessage_GivesTheSameBytes(string name)
    {
        byte[] original = DnsMessageWriter.Write(Build(name));

        byte[] rewritten = DnsMessageWriter.Write(DnsMessageReader.Read(original));

        Assert.Equal(original, rewritten);
    }

    /// <summary>
    ///     This test checks that writing from several threads at once gives the recorded bytes for
    ///     every message, so any per-thread writer state stays per thread.
    /// </summary>
    [Fact]
    public void Write_FromManyThreadsAtOnce_GivesTheRecordedBytes()
    {
        Parallel.For(
            0,
            400,
            index =>
            {
                (string name, Func<DnsMessage> build) = DnsMessageGoldenCorpus.All[
                    index % DnsMessageGoldenCorpus.All.Count
                ];
                Assert.Equal(
                    DnsMessageGoldenExpected.ByName[name],
                    Describe(DnsMessageWriter.Write(build()))
                );
            }
        );
    }

    /// <summary>
    ///     This method builds the corpus message called <paramref name="name" />.
    /// </summary>
    private static DnsMessage Build(string name) =>
        DnsMessageGoldenCorpus.All.Single(entry => entry.Name == name).Build();

    /// <summary>
    ///     This method renders bytes the way <see cref="DnsMessageGoldenExpected" /> records them.
    /// </summary>
    private static string Describe(byte[] bytes) =>
        bytes.Length > 4096
            ? $"sha256:{bytes.Length}:{Convert.ToHexString(SHA256.HashData(bytes))}"
            : Convert.ToHexString(bytes);
}
