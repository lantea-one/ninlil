using System.Runtime.Serialization;
using Ninlil.Common.Extension.String;
using Ninlil.Common.Extension.Tests.TestSupport;
using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.String;

/// <summary>
///     These tests parse strings into a sample enum with <see cref="ToEnumStringExtension" /> and
///     the default attribute checks: member names in any case, attribute values, blank input, and
///     unmatched input (which gives null, never the zero member), number strings (any value in range of
///     the underlying type) and comma lists (flags enums only).
/// </summary>
public class ToEnumStringExtensionTests
{
    /// <summary>
    ///     A sample enum decorated with <see cref="EnumMemberAttribute" /> on one member.
    /// </summary>
    private enum SampleEnum
    {
        [EnumMember(Value = "first-value")]
        First,

        Second,
    }

    [Fact]
    public void ToEnumParsesExactMemberNameDirectly()
    {
        System.Enum result = "Second".ToEnum(
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks,
            typeof(SampleEnum)
        );

        Assert.Equal(SampleEnum.Second, result);
    }

    [Fact]
    public void ToEnumParsesCaseInsensitively()
    {
        System.Enum result = "second".ToEnum(
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks,
            typeof(SampleEnum)
        );

        Assert.Equal(SampleEnum.Second, result);
    }

    [Fact]
    public void ToEnumParsesAttributeDrivenValue()
    {
        System.Enum result = "first-value".ToEnum(
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks,
            typeof(SampleEnum)
        );

        Assert.Equal(SampleEnum.First, result);
    }

    [Fact]
    public void ToEnumOnNullOrEmptyStringReturnsNull()
    {
        Assert.Null(
            ((string)null).ToEnum(
                GetEnumDescriptionsTypeExtension.DefaultAttributeChecks,
                typeof(SampleEnum)
            )
        );
        Assert.Null(
            "".ToEnum(GetEnumDescriptionsTypeExtension.DefaultAttributeChecks, typeof(SampleEnum))
        );
    }

    [Fact]
    public void ToEnum_WithUnmatchedValue_ReturnsNull()
    {
        System.Enum result = "does-not-exist".ToEnum(
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks,
            typeof(SampleSecondValueEnum)
        );

        Assert.Null(result);
    }

    [Fact]
    public void TryToEnum_WithUnmatchedValue_ReturnsFalse()
    {
        bool success = "does-not-exist".TryToEnum(
            out System.Enum result,
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks,
            typeof(SampleSecondValueEnum)
        );

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void ToEnumGeneric_WithUnmatchedValue_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            "nope".ToEnum<SampleSecondValueEnum>(
                GetEnumDescriptionsTypeExtension.DefaultAttributeChecks
            )
        );
    }

    [Fact]
    public void ToEnumGeneric_WithABlankValue_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            "".ToEnum<SampleSecondValueEnum>(
                GetEnumDescriptionsTypeExtension.DefaultAttributeChecks
            )
        );
    }

    [Fact]
    public void ToEnum_WithTheSecondMembersDescription_ReturnsTheSecondMember()
    {
        System.Enum result = "second-value".ToEnum(
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks,
            typeof(SampleSecondValueEnum)
        );

        Assert.Equal(SampleSecondValueEnum.Second, result);
    }

    [Fact]
    public void ToEnum_WithANullableType_UnwrapsItAndMatches()
    {
        System.Enum result = "Second".ToEnum(
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks,
            typeof(SampleSecondValueEnum?)
        );

        Assert.Equal(SampleSecondValueEnum.Second, result);
    }

    [Fact]
    public void ToEnum_WithACommaListOnANonFlagsEnum_ReturnsNull()
    {
        System.Enum result = "First,Second".ToEnum(
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks,
            typeof(SampleSecondValueEnum)
        );

        Assert.Null(result);
    }

    [Fact]
    public void ToEnum_WithACommaListOnAFlagsEnum_ReturnsTheCombinedValue()
    {
        System.Enum result = "One,Two".ToEnum(
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks,
            typeof(SampleFlagsEnum)
        );

        Assert.Equal(SampleFlagsEnum.One | SampleFlagsEnum.Two, result);
    }

    [Fact]
    public void ToEnum_WithANumberString_ReturnsThatMember()
    {
        System.Enum result = "1".ToEnum(
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks,
            typeof(SampleSecondValueEnum)
        );

        Assert.Equal(SampleSecondValueEnum.Second, result);
    }

    /// <summary>
    ///     Wire enums carry codes with no member; the number must read back as that code.
    /// </summary>
    [Fact]
    public void ToEnum_WithAnUndefinedNumberInRange_ReturnsThatValue()
    {
        System.Enum result = "99".ToEnum(
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks,
            typeof(SampleWireEnum)
        );

        Assert.Equal((SampleWireEnum)99, result);
    }

    [Fact]
    public void ToEnum_WithANumberOutOfRangeOfTheUnderlyingType_ReturnsNull()
    {
        System.Enum result = "70000".ToEnum(
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks,
            typeof(SampleWireEnum)
        );

        Assert.Null(result);
    }

    [Fact]
    public void ToEnum_WithANullType_ReturnsNull()
    {
        Assert.Null("Second".ToEnum(GetEnumDescriptionsTypeExtension.DefaultAttributeChecks, null));
    }

    [Fact]
    public void ToEnum_WithANonEnumType_ReturnsNull()
    {
        Assert.Null(
            "Second".ToEnum(GetEnumDescriptionsTypeExtension.DefaultAttributeChecks, typeof(int))
        );
    }

    [Fact]
    public void ToEnumGenericParsesToTheTypedEnum()
    {
        SampleEnum result = "Second".ToEnum<SampleEnum>(
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks
        );

        Assert.Equal(SampleEnum.Second, result);
    }

    [Fact]
    public void TryToEnumOnValidValueReturnsTrueAndTheParsedEnum()
    {
        bool success = "Second".TryToEnum(
            out System.Enum result,
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks,
            typeof(SampleEnum)
        );

        Assert.True(success);
        Assert.Equal(SampleEnum.Second, result);
    }

    [Fact]
    public void TryToEnumOnNullOrEmptyStringReturnsFalse()
    {
        bool success = ((string)null).TryToEnum(
            out System.Enum result,
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks,
            typeof(SampleEnum)
        );

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void TryToEnumGenericOnValidValueReturnsTrueAndTheTypedEnum()
    {
        bool success = "Second".TryToEnum(
            out SampleEnum result,
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks
        );

        Assert.True(success);
        Assert.Equal(SampleEnum.Second, result);
    }
}
