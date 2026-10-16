using System.Net;
using System.Net.Mail;
using Ninlil.Common.Extension.String;
using Ninlil.Common.Extension.Tests.TestSupport;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.String;

/// <summary>
///     These tests convert strings to many target types with <c>GetTypeValueStringExtension</c>
///     (the class in <c>GetTypedValueStringExtension.cs</c>; the file and class names differ):
///     null-like tokens, scalars, enums, collections, dictionaries, tuples, custom delimiters and
///     quoted input, plus the <c>Can</c>/<c>Try</c> variants.
/// </summary>
public class GetTypedValueStringExtensionTests
{
    private enum SampleEnum
    {
        First,
        Second,
    }

    [Fact]
    public void GetTypedValueOnStringReturnsTheStringUnchanged()
    {
        Assert.Equal("hello", "hello".GetTypedValue<string>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nil")]
    [InlineData("null")]
    [InlineData("NULL")]
    public void GetTypedValueOnNullLikeTokensReturnsDefaultForAValueType(string value)
    {
        Assert.Equal(0, value.GetTypedValue<int>());
    }

    [Fact]
    public void GetTypedValueOnNullLikeTokenReturnsNullForANullableValueType()
    {
        Assert.Null("null".GetTypedValue<int?>());
    }

    [Fact]
    public void GetTypedValueOnEnumParsesByMemberName()
    {
        Assert.Equal(SampleEnum.Second, "Second".GetTypedValue<SampleEnum>());
    }

    [Fact]
    public void GetTypedValue_OnAnEnumDescription_ReturnsThatMember()
    {
        Assert.Equal(
            SampleSecondValueEnum.Second,
            "second-value".GetTypedValue<SampleSecondValueEnum>()
        );
    }

    [Fact]
    public void GetTypedValue_OnAnUnknownEnumName_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => "nope".GetTypedValue(typeof(SampleSecondValueEnum)));
    }

    [Fact]
    public void TryGetTypedValue_OnAnUnknownEnumName_ReturnsFalse()
    {
        Assert.False("nope".TryGetTypedValue(out SampleSecondValueEnum _));
    }

    [Fact]
    public void GetTypedValue_OnANullableEnumName_ReturnsTheMember()
    {
        Assert.Equal(
            SampleSecondValueEnum.Second,
            "Second".GetTypedValue(typeof(SampleSecondValueEnum?))
        );
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("yes", true)]
    [InlineData("t", true)]
    [InlineData("y", true)]
    [InlineData("1", true)]
    [InlineData("on", true)]
    [InlineData("ON", true)]
    [InlineData(" yes ", true)]
    [InlineData("false", false)]
    [InlineData("no", false)]
    [InlineData("off", false)]
    [InlineData("f", false)]
    [InlineData("n", false)]
    [InlineData("0", false)]
    public void GetTypedValueOnBoolean_WithABooleanWord_ReadsItWhole(string value, bool expected)
    {
        Assert.Equal(expected, value.GetTypedValue<bool>());
    }

    /// <summary>
    ///     Text that only contains a boolean word ("not" holds <c>t</c>, "maybe" holds <c>y</c>) or is
    ///     no boolean word at all is a format error, so <c>TryGetTypedValue</c> reports failure.
    /// </summary>
    [Theory]
    [InlineData("not")]
    [InlineData("maybe")]
    [InlineData("bogus")]
    [InlineData("monday")]
    [InlineData("10")]
    public void GetTypedValueOnBoolean_WithTextThatIsNoBooleanWord_ThrowsFormatException(
        string value
    )
    {
        Assert.Throws<FormatException>(() => value.GetTypedValue<bool>());
        Assert.False(value.TryGetTypedValue(out bool _));
    }

    [Fact]
    public void GetTypedValueOnByteParsesHexadecimal()
    {
        Assert.Equal((byte)0x1A, "1A".GetTypedValue<byte>());
    }

    [Fact]
    public void GetTypedValueOnByteArrayParsesBase64()
    {
        byte[] expected = [1, 2, 3];

        Assert.Equal(expected, Convert.ToBase64String(expected).GetTypedValue<byte[]>());
    }

    [Fact]
    public void GetTypedValueOnCharParsesASingleCharacter()
    {
        Assert.Equal('x', "x".GetTypedValue<char>());
    }

    [Fact]
    public void GetTypedValueOnCharArrayReturnsEachCharacter()
    {
        Assert.Equal(['a', 'b', 'c'], "abc".GetTypedValue<char[]>());
    }

    [Fact]
    public void GetTypedValueOnDateOnlyParsesTheDate()
    {
        Assert.Equal(new DateOnly(2026, 1, 2), "2026-01-02".GetTypedValue<DateOnly>());
    }

    [Fact]
    public void GetTypedValueOnDateTimeParsesTheDateTime()
    {
        DateTime result = "2026-01-02T03:04:05".GetTypedValue<DateTime>();

        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), result);
    }

    [Fact]
    public void GetTypedValueOnDateTimeOffsetParsesTheOffsetDateTime()
    {
        DateTimeOffset result = "2026-01-02T03:04:05+00:00".GetTypedValue<DateTimeOffset>();

        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), result);
    }

    [Fact]
    public void GetTypedValueOnDecimalParsesTheValue()
    {
        Assert.Equal(3.14m, "3.14".GetTypedValue<decimal>());
    }

    [Fact]
    public void GetTypedValueOnDoubleParsesTheValue()
    {
        Assert.Equal(3.14, "3.14".GetTypedValue<double>());
    }

    [Fact]
    public void GetTypedValueOnFloatParsesTheValue()
    {
        Assert.Equal(3.14f, "3.14".GetTypedValue<float>());
    }

    [Fact]
    public void GetTypedValueOnGuidParsesTheValue()
    {
        Guid expected = Guid.NewGuid();

        Assert.Equal(expected, expected.ToString().GetTypedValue<Guid>());
    }

    [Fact]
    public void GetTypedValueOnIntParsesTheValue()
    {
        Assert.Equal(42, "42".GetTypedValue<int>());
    }

    [Fact]
    public void GetTypedValueOnIpAddressParsesTheValue()
    {
        Assert.Equal(IPAddress.Parse("192.168.1.1"), "192.168.1.1".GetTypedValue<IPAddress>());
    }

    [Fact]
    public void GetTypedValueOnLongParsesTheValue()
    {
        Assert.Equal(9876543210L, "9876543210".GetTypedValue<long>());
    }

    [Fact]
    public void GetTypedValueOnMailAddressParsesTheValue()
    {
        MailAddress result = "user@example.com".GetTypedValue<MailAddress>();

        Assert.Equal("user@example.com", result.Address);
    }

    [Fact]
    public void GetTypedValueOnShortParsesTheValue()
    {
        Assert.Equal((short)42, "42".GetTypedValue<short>());
    }

    [Fact]
    public void GetTypedValueOnTimeOnlyParsesTheValue()
    {
        Assert.Equal(new TimeOnly(3, 4, 5), "03:04:05".GetTypedValue<TimeOnly>());
    }

    [Fact]
    public void GetTypedValueOnTimeSpanParsesTheValue()
    {
        Assert.Equal(TimeSpan.FromMinutes(90), "01:30:00".GetTypedValue<TimeSpan>());
    }

    [Fact]
    public void GetTypedValueOnUnsignedIntegersParsesEachWidth()
    {
        Assert.Equal(42U, "42".GetTypedValue<uint>());
        Assert.Equal(42UL, "42".GetTypedValue<ulong>());
        Assert.Equal((ushort)42, "42".GetTypedValue<ushort>());
    }

    [Fact]
    public void GetTypedValueOnListOfIntSplitsByDelimiterAndParsesEachElement()
    {
        List<int> result = "1,2,3".GetTypedValue<List<int>>();

        Assert.Equal([1, 2, 3], result);
    }

    [Fact]
    public void GetTypedValueOnArrayOfIntSplitsByDelimiterAndParsesEachElement()
    {
        int[] result = "1,2,3".GetTypedValue<int[]>();

        Assert.Equal([1, 2, 3], result);
    }

    [Fact]
    public void GetTypedValueOnListHonorsACustomDelimiter()
    {
        List<int> result = "1|2|3".GetTypedValue<List<int>>("|");

        Assert.Equal([1, 2, 3], result);
    }

    /// <summary>
    ///     The non-generic entry point creates a <c>Dictionary&lt;TKey, TValue&gt;</c> of the requested
    ///     types by reflection and fills it through <c>Add</c>. LINQ's <c>ToDictionary</c> would infer
    ///     <see cref="object" /> for both, since <c>GetTypedValue(Type, ...)</c> returns
    ///     <see cref="object" />. The result must be a <c>Dictionary&lt;string, int&gt;</c>.
    /// </summary>
    [Fact]
    public void GetTypedValueOnDictionaryParsesBraceDelimitedKeyValuePairsAsATypedDictionary()
    {
        object result = "{a:1,b:2}".GetTypedValue(typeof(Dictionary<string, int>));

        Dictionary<string, int> dictionary = Assert.IsType<Dictionary<string, int>>(result);
        Assert.Equal(1, dictionary["a"]);
        Assert.Equal(2, dictionary["b"]);
    }

    /// <summary>
    ///     Because the non-generic entry point produces a typed dictionary (see above), the generic
    ///     <c>GetTypedValue&lt;TValue&gt;()</c> cast to <c>TValue</c> succeeds instead of throwing
    ///     <see cref="InvalidCastException" />.
    /// </summary>
    [Fact]
    public void GetTypedValueGenericOnDictionaryReturnsATypedDictionary()
    {
        Dictionary<string, int> result = "{a:1,b:2}".GetTypedValue<Dictionary<string, int>>();

        Assert.Equal(1, result["a"]);
        Assert.Equal(2, result["b"]);
    }

    [Fact]
    public void GetTypedValueOnDictionaryThrowsWhenNotBraceDelimited()
    {
        Assert.Throws<ArgumentException>(() => "a:1,b:2".GetTypedValue<Dictionary<string, int>>());
    }

    [Fact]
    public void GetTypedValueOnDictionaryWithNullTokenReturnsNull()
    {
        Assert.Null("null".GetTypedValue<Dictionary<string, int>>());
    }

    [Fact]
    public void GetTypedValueOnDictionaryWithEmptyBracesReturnsAnEmptyDictionary()
    {
        Dictionary<string, int> result = "{}".GetTypedValue<Dictionary<string, int>>();

        Assert.Empty(result);
    }

    /// <summary>
    ///     A single-element tuple is not supported: the tuple branch requires at least two element
    ///     types, because its parenthesized syntax cannot tell a one-element tuple from a
    ///     parenthesized value. It always throws for that shape.
    /// </summary>
    [Fact]
    public void GetTypedValueOnASingleElementTupleThrowsDueToTheElementCountGuard()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            "(5)".GetTypedValue<Tuple<int>>()
        );
        Assert.Contains("Expected at least two element types", exception.Message);
    }

    /// <summary>
    ///     <c>IsTypeExtension.IsTuple</c> recognizes every tuple arity (see <c>IsTypeExtensionTests</c>)
    ///     and the branch constructs a tuple instance rather than returning an untyped
    ///     <see cref="System.Collections.Generic.IEnumerable{T}" />, so a multi-element tuple parses.
    /// </summary>
    [Fact]
    public void GetTypedValueOnAMultiElementTupleParsesEachElement()
    {
        Tuple<int, string, bool> result = "(5,hello,true)".GetTypedValue<
            Tuple<int, string, bool>
        >();

        Assert.Equal(5, result.Item1);
        Assert.Equal("hello", result.Item2);
        Assert.True(result.Item3);
    }

    /// <summary>
    ///     The tuple branch's element-count check splits on the <c>delimiter</c> argument rather than
    ///     a hardcoded comma, so a custom delimiter such as <c>"]-["</c> lets an element contain a
    ///     comma.
    /// </summary>
    [Fact]
    public void GetTypedValueOnATupleWithACustomDelimiterParsesEachElement()
    {
        Tuple<int, string, bool> result = "(5]-[hello, world]-[true)".GetTypedValue<
            Tuple<int, string, bool>
        >("]-[");

        Assert.Equal(5, result.Item1);
        Assert.Equal("hello, world", result.Item2);
        Assert.True(result.Item3);
    }

    [Fact]
    public void CanGetTypedValueReturnsTrueForAParsableValue()
    {
        Assert.True("42".CanGetTypedValue<int>());
    }

    [Fact]
    public void CanGetTypedValueReturnsFalseForAnUnparsableValue()
    {
        Assert.False("not-a-number".CanGetTypedValue<int>());
    }

    [Fact]
    public void TryGetTypedValueReturnsTrueAndTheParsedValueForAParsableInput()
    {
        bool success = "42".TryGetTypedValue(out int result);

        Assert.True(success);
        Assert.Equal(42, result);
    }

    [Fact]
    public void TryGetTypedValueReturnsFalseAndTheDefaultValueForAnUnparsableInput()
    {
        bool success = "not-a-number".TryGetTypedValue(out int result);

        Assert.False(success);
        Assert.Equal(0, result);
    }

    [Fact]
    public void TryGetTypedValueForAReferenceTypeReturnsFalseAndNullForAnUnparsableInput()
    {
        bool success = "not-a-guid-or-object".TryGetTypedValue(out Guid result);

        Assert.False(success);
        Assert.Equal(Guid.Empty, result);
    }

    /// <summary>
    ///     <c>IsTypeExtension</c> does not report <see cref="object" /> as a collection (see
    ///     <c>IsTypeExtensionTests</c>), so <c>GetTypedValue(instance, typeof(object), ...)</c> reaches
    ///     its fallback and returns the raw string instead of taking the list branch and throwing.
    /// </summary>
    [Fact]
    public void GetTypedValueOnObjectTypePassesTheRawStringThrough()
    {
        object result = "raw-value".GetTypedValue(typeof(object));

        Assert.Equal("raw-value", result);
    }

    /// <summary>
    ///     <c>GetTypedValue</c> runs every input through <c>SanitizeForValueTyping()</c> (default
    ///     <c>depth: 13</c>) first, and that method strips the quote pair only once (see
    ///     <c>SanitizeStringExtensionTests</c>), so a short quoted string is unwrapped instead of
    ///     throwing.
    /// </summary>
    [Fact]
    public void GetTypedValueOnAShortQuotedStringUnwrapsTheQuotes()
    {
        Assert.Equal("hello", "\"hello\"".GetTypedValue<string>());
    }
}
