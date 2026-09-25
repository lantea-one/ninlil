using Ninlil.Common.Extension.Object;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Object;

/// <summary>
///     These tests check the string <see cref="ToStringValueObjectExtension.ToStringValue" /> gives
///     for each branch: scalars, dates, enums, arrays, lists, dictionaries, tuples, a plain class
///     and null.
/// </summary>
public class ToStringValueObjectExtensionTests
{
    /// <summary>
    ///     A minimal enum used to exercise the enum branch of <c>ToStringValue</c>.
    /// </summary>
    private enum SampleEnum
    {
        First,
        Second,
    }

    /// <summary>
    ///     A minimal plain class with no custom <see cref="System.ComponentModel.TypeConverter" />, used to
    ///     exercise the class fallback branch of <c>ToStringValue</c>.
    /// </summary>
    private class PlainClass
    {
        public string Name { get; set; }
    }

    [Fact]
    public void ToStringValueOnStringTrimsWhitespace()
    {
        Assert.Equal("hello", "  hello  ".ToStringValue());
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void ToStringValueOnBooleanIsLowercase(bool value, string expected)
    {
        Assert.Equal(expected, value.ToStringValue());
    }

    [Fact]
    public void ToStringValueOnByteFormatsAsTwoDigitHex()
    {
        byte value = 0x0A;

        Assert.Equal("0A", value.ToStringValue());
    }

    [Fact]
    public void ToStringValueOnByteArrayFormatsAsBase64()
    {
        byte[] value = [1, 2, 3];

        Assert.Equal(Convert.ToBase64String(value), value.ToStringValue());
    }

    [Fact]
    public void ToStringValueOnCharReturnsSingleCharacterString()
    {
        Assert.Equal("x", 'x'.ToStringValue());
    }

    [Fact]
    public void ToStringValueOnDateOnlyUsesRoundTripFormat()
    {
        DateOnly value = new(2026, 1, 2);

        Assert.Equal(value.ToString("O"), value.ToStringValue());
    }

    [Fact]
    public void ToStringValueOnDateTimeUsesRoundTripFormat()
    {
        DateTime value = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        Assert.Equal(value.ToString("O"), value.ToStringValue());
    }

    [Fact]
    public void ToStringValueOnDateTimeOffsetUsesRoundTripFormat()
    {
        DateTimeOffset value = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        Assert.Equal(value.ToString("O"), value.ToStringValue());
    }

    [Fact]
    public void ToStringValueOnGuidIsLowercase()
    {
        Guid value = Guid.NewGuid();

        Assert.Equal(value.ToString().ToLower(), value.ToStringValue());
    }

    [Fact]
    public void ToStringValueOnEnumUsesToDescription()
    {
        Assert.Equal("First", SampleEnum.First.ToStringValue());
    }

    [Fact]
    public void ToStringValueOnTimeOnlyUsesRoundTripFormat()
    {
        TimeOnly value = new(3, 4, 5);

        Assert.Equal(value.ToString("O"), value.ToStringValue());
    }

    [Fact]
    public void ToStringValueOnTimeSpanUsesConstantFormat()
    {
        TimeSpan value = TimeSpan.FromMinutes(90);

        Assert.Equal(value.ToString("c"), value.ToStringValue());
    }

    [Fact]
    public void ToStringValueOnReferenceTypeArrayJoinsElementsWithComma()
    {
        string[] value = ["a", "b", "c"];

        Assert.Equal("a,b,c", value.ToStringValue());
    }

    [Fact]
    public void ToStringValueOnReferenceTypeListJoinsElementsWithComma()
    {
        List<string> value = ["a", "b"];

        Assert.Equal("a,b", value.ToStringValue());
    }

    /// <summary>
    ///     The private <c>ListToString</c> helper enumerates the non-generic
    ///     <see cref="System.Collections.IEnumerable" />, so a value-type array is converted instead of
    ///     throwing.
    /// </summary>
    [Fact]
    public void ToStringValueOnValueTypeArrayJoinsElementsWithComma()
    {
        int[] value = [1, 2, 3];

        Assert.Equal("1,2,3", value.ToStringValue());
    }

    /// <summary>
    ///     The private <c>DictionaryToString</c> helper reads the value through the non-generic
    ///     <see cref="System.Collections.IDictionary" />, so a dictionary whose key and value types are
    ///     not <see cref="object" /> still renders its entries.
    /// </summary>
    [Fact]
    public void ToStringValueOnTypedDictionaryRendersItsEntries()
    {
        Dictionary<string, int> value = new() { ["a"] = 1 };

        Assert.Equal("{a:1}", value.ToStringValue());
    }

    /// <summary>
    ///     <c>IsTypeExtension.IsTuple</c> recognizes every tuple arity, so a multi-element tuple takes the
    ///     tuple branch and is formatted through <see cref="System.Runtime.CompilerServices.ITuple" />
    ///     rather than the tuple's own <see cref="object.ToString" />.
    /// </summary>
    [Fact]
    public void ToStringValueOnMultiElementTupleJoinsElementsWithComma()
    {
        Tuple<int, string> value = new(1, "a");

        Assert.Equal("(1,a)", value.ToStringValue());
    }

    /// <summary>
    ///     A plain class with no custom <see cref="System.ComponentModel.TypeConverter" /> reaches the
    ///     <c>TypeDescriptor.GetConverter(...).CanConvertTo(typeof(string))</c> branch. The base converter
    ///     always answers <see langword="true" /> for <see cref="string" /> and calls
    ///     <see cref="object.ToString" />, so the later property-dictionary branch is unreachable for an
    ///     ordinary class.
    /// </summary>
    [Fact]
    public void ToStringValueOnPlainClassUsesDefaultObjectToStringNotPropertyDictionary()
    {
        PlainClass value = new() { Name = "sample" };

        string result = value.ToStringValue();

        Assert.Equal(value.ToString(), result);
        Assert.DoesNotContain("sample", result);
    }

    [Fact]
    public void ToStringValueOnNullReturnsNull()
    {
        object value = null;

        Assert.Null(value.ToStringValue());
    }
}
