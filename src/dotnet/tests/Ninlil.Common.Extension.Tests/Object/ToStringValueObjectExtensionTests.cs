using Ninlil.Common.Extension.Object;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Object;

/// <summary>
///     This class tests <see cref="ToStringValueObjectExtension" />.
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
    ///     The private <c>ListToString</c> helper enumerates through the non-generic
    ///     <see cref="System.Collections.IEnumerable" /> interface, which every array/list implements
    ///     regardless of its element type, so a value-type array correctly stringifies its elements
    ///     instead of throwing.
    /// </summary>
    [Fact]
    public void ToStringValueOnValueTypeArrayJoinsElementsWithComma()
    {
        int[] value = [1, 2, 3];

        Assert.Equal("1,2,3", value.ToStringValue());
    }

    /// <summary>
    ///     The private <c>DictionaryToString</c> helper reads the incoming value through the non-generic
    ///     <see cref="System.Collections.IDictionary" /> interface, which every closed
    ///     <see cref="Dictionary{TKey,TValue}" /> implements regardless of its actual key/value types, so
    ///     <c>ToStringValue</c> renders its actual entries.
    /// </summary>
    [Fact]
    public void ToStringValueOnTypedDictionaryRendersItsEntries()
    {
        Dictionary<string, int> value = new() { ["a"] = 1 };

        Assert.Equal("{a:1}", value.ToStringValue());
    }

    /// <summary>
    ///     <c>IsTypeExtension.IsTuple</c> now recognizes every real tuple arity, so the outer switch's
    ///     dedicated tuple branch correctly triggers for a multi-element tuple, formatting its elements as
    ///     a bare comma-separated list (via <see cref="System.Runtime.CompilerServices.ITuple" />) rather
    ///     than falling through to the tuple's own built-in <see cref="object.ToString" />.
    /// </summary>
    [Fact]
    public void ToStringValueOnMultiElementTupleJoinsElementsWithComma()
    {
        Tuple<int, string> value = new(1, "a");

        Assert.Equal("(1,a)", value.ToStringValue());
    }

    /// <summary>
    ///     A plain class with no custom <see cref="System.ComponentModel.TypeConverter" /> reaches the
    ///     <c>TypeDescriptor.GetConverter(...).CanConvertTo(typeof(string))</c> branch, and the base
    ///     <see cref="System.ComponentModel.TypeConverter" /> always answers <see langword="true" /> for a
    ///     <see cref="string" /> destination type, whose default <c>ConvertToString</c> implementation just
    ///     calls the instance's own <see cref="object.ToString" />. In practice this means the later
    ///     "convert public properties to a dictionary" class fallback branch is unreachable for an ordinary
    ///     class, since the <see cref="System.ComponentModel.TypeConverter" /> branch always intercepts
    ///     first.
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
