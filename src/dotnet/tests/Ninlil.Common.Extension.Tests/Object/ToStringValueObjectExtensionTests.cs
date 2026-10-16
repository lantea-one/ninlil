using System.Net;
using Ninlil.Common.Extension.Object;
using Ninlil.Common.Extension.String;
using Ninlil.Common.Extension.Tests.TestSupport;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Object;

/// <summary>
///     These tests check the string <see cref="ToStringValueObjectExtension.ToStringValue" /> gives
///     for each branch: scalars, dates, enums, arrays, lists, dictionaries, tuples, classes (with and
///     without a <c>ToString</c> override, with a reference cycle and with an indexer) and null.
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
    ///     A class that points at itself, used to prove the property-dictionary branch stops at a
    ///     reference it is already rendering.
    /// </summary>
    private class SelfReferencingClass
    {
        public string Name { get; set; }

        public SelfReferencingClass Self { get; set; }
    }

    /// <summary>
    ///     A class with an indexer, which the property-dictionary branch must skip because a property
    ///     with index parameters cannot be read without an index.
    /// </summary>
    private class IndexedClass
    {
        public string Name { get; set; }

        public string this[int index] => "item" + index;
    }

    /// <summary>
    ///     A record, which overrides <c>ToString</c>, so it prints through that override.
    /// </summary>
    private record SampleRecord(string Name, int Number);

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

    /// <summary>
    ///     A member with an <c>[EnumMember]</c> value prints that value, not the member name, and the
    ///     text parses back to the member through <c>GetTypedValue</c>.
    /// </summary>
    [Fact]
    public void ToStringValueOnEnumWithAnEnumMemberValue_PrintsTheDescriptionAndRoundTrips()
    {
        string text = SampleSecondValueEnum.Second.ToStringValue();

        Assert.Equal("second-value", text);
        Assert.Equal(
            SampleSecondValueEnum.Second,
            text.GetTypedValue(typeof(SampleSecondValueEnum))
        );
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
    ///     A plain class that does not override <c>ToString</c> prints its public properties in
    ///     declaration order, not the type name.
    /// </summary>
    [Fact]
    public void ToStringValueOnAPlainClass_PrintsItsPropertiesInDeclarationOrder()
    {
        SampleModel value = new() { Name = "n", Number = 7 };

        Assert.Equal("{Name:n,Number:7,FixedValue:fixed}", value.ToStringValue());
    }

    [Fact]
    public void ToStringValueOnARecord_UsesItsToStringOverride()
    {
        SampleRecord value = new("n", 7);

        Assert.Equal(value.ToString(), value.ToStringValue());
    }

    [Fact]
    public void ToStringValueOnAnIpAddress_UsesItsToStringOverride()
    {
        Assert.Equal("192.0.2.1", IPAddress.Parse("192.0.2.1").ToStringValue());
    }

    /// <summary>
    ///     A class that refers to itself finishes: the inner reference is written as the type name.
    ///     Without the guard the recursion would overflow the stack.
    /// </summary>
    [Fact]
    public void ToStringValueOnAClassWithAReferenceCycle_WritesTheTypeNameForTheRepeatedReference()
    {
        SelfReferencingClass value = new() { Name = "n" };
        value.Self = value;

        Assert.Equal("{Name:n,Self:SelfReferencingClass}", value.ToStringValue());
    }

    [Fact]
    public void ToStringValueOnAClassWithAnIndexer_SkipsTheIndexer()
    {
        IndexedClass value = new() { Name = "n" };

        Assert.Equal("{Name:n}", value.ToStringValue());
    }

    [Fact]
    public void ToStringValueOnNullReturnsNull()
    {
        object value = null;

        Assert.Null(value.ToStringValue());
    }
}
