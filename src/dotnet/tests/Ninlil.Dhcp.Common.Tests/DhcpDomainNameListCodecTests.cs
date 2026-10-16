using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Tests;

/// <summary>
///     This test class covers <see cref="DhcpDomainNameListCodec" />: the RFC 1035 Section 3.1
///     uncompressed wire form of the DHCPv6 Domain Search List (RFC 3646 Section 4), in both
///     directions, and each way a value can be malformed. Pure functions, no I/O.
/// </summary>
public class DhcpDomainNameListCodecTests
{
    /// <summary>
    ///     This test proves two names encode to labels back to back and decode back, including the
    ///     optional trailing dot.
    /// </summary>
    [Fact]
    public void TryEncode_AndDecode_RoundTripTwoNames()
    {
        Assert.True(
            DhcpDomainNameListCodec.TryEncode(["example.com", "lan."], out byte[] value, out _)
        );

        byte[] expected =
        [
            .. new byte[] { 7 },
            .. "example"u8.ToArray(),
            .. new byte[] { 3 },
            .. "com"u8.ToArray(),
            0,
            .. new byte[] { 3 },
            .. "lan"u8.ToArray(),
            0,
        ];
        Assert.Equal(expected, value);

        Assert.True(
            DhcpDomainNameListCodec.TryDecode(value, out IReadOnlyList<string> names, out _)
        );
        Assert.Equal(["example.com", "lan"], names);
    }

    /// <summary>
    ///     This test proves white space around a name is trimmed.
    /// </summary>
    [Fact]
    public void TryEncode_ForANameWithSurroundingSpaces_TrimsIt()
    {
        Assert.True(DhcpDomainNameListCodec.TryEncode([" lan "], out byte[] value, out _));
        Assert.Equal(new byte[] { 3, (byte)'l', (byte)'a', (byte)'n', 0 }, value);
    }

    /// <summary>
    ///     This test proves each invalid input to the encoder fails with a message.
    /// </summary>
    [Theory]
    [MemberData(nameof(BadNameLists))]
    public void TryEncode_ForABadList_Fails(string[] names)
    {
        Assert.False(DhcpDomainNameListCodec.TryEncode(names, out byte[] value, out string error));
        Assert.Null(value);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>
    ///     This test proves a null list fails like an empty one.
    /// </summary>
    [Fact]
    public void TryEncode_ForANullList_Fails() =>
        Assert.False(DhcpDomainNameListCodec.TryEncode(null, out _, out _));

    /// <summary>
    ///     This test proves a 63-octet label passes, the edge of the label limit.
    /// </summary>
    [Fact]
    public void TryEncode_ForA63OctetLabel_Succeeds() =>
        Assert.True(DhcpDomainNameListCodec.TryEncode([new string('a', 63)], out _, out _));

    /// <summary>
    ///     This test proves each invalid input to the decoder fails with a message.
    /// </summary>
    [Theory]
    [MemberData(nameof(BadWireValues))]
    public void TryDecode_ForABadValue_Fails(byte[] value)
    {
        Assert.False(
            DhcpDomainNameListCodec.TryDecode(
                value,
                out IReadOnlyList<string> names,
                out string error
            )
        );
        Assert.Null(names);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>
    ///     This test proves a 255-octet wire name decodes and a 256-octet one fails.
    /// </summary>
    [Fact]
    public void TryDecode_At255And256Octets_AcceptsThenRejects()
    {
        // Three 63-octet labels take 3 x 64 = 192 octets. The last label takes 1 + n and the
        // root label 1, so n = 61 makes 255 and n = 62 makes 256.
        byte[] Build(int lastLabel) =>
            [
                .. Enumerable
                    .Repeat(new byte[] { 63 }.Concat(Enumerable.Repeat((byte)'a', 63)), 3)
                    .SelectMany(label => label),
                .. new byte[] { (byte)lastLabel },
                .. Enumerable.Repeat((byte)'a', lastLabel),
                0,
            ];

        Assert.True(DhcpDomainNameListCodec.TryDecode(Build(61), out _, out _));
        Assert.False(DhcpDomainNameListCodec.TryDecode(Build(62), out _, out _));
    }

    /// <summary>
    ///     Gets the name lists the encoder must refuse: empty list, empty name, a 64-octet label,
    ///     a 256-octet name, an empty label and a non-ASCII name.
    /// </summary>
    public static TheoryData<string[]> BadNameLists =>
        new()
        {
            Array.Empty<string>(),
            new[] { string.Empty },
            new[] { new string('a', 64) },
            new[] { string.Join('.', Enumerable.Repeat(new string('a', 63), 4)) },
            new[] { "a..b" },
            new[] { "bücher.example" },
        };

    /// <summary>
    ///     Gets the wire values the decoder must refuse: empty, a compression pointer, trailing
    ///     garbage after a complete name, a missing root label, a label past the end and a control
    ///     character in a label.
    /// </summary>
    public static TheoryData<byte[]> BadWireValues =>
        new()
        {
            Array.Empty<byte>(),
            new byte[] { 0xC0, 0x0C },
            new byte[] { 3, (byte)'l', (byte)'a', (byte)'n', 0, 3, (byte)'x' },
            new byte[] { 3, (byte)'l', (byte)'a', (byte)'n' },
            new byte[] { 9, (byte)'l', 0 },
            new byte[] { 3, (byte)'l', 0x07, (byte)'n', 0 },
        };
}
