using System.Runtime.Serialization;
using Ninlil.Common.Extension.String;
using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.String;

/// <summary>
///     These tests parse strings into a sample enum with <see cref="ToEnumStringExtension" /> and
///     the default attribute checks: member names in any case, attribute values, blank input, and
///     unmatched input (which gives the zero member, not null).
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
    public void ToEnumOnUnmatchedValueReturnsDefault()
    {
        System.Enum result = "does-not-exist".ToEnum(
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks,
            typeof(SampleEnum)
        );

        Assert.Equal(SampleEnum.First, result);
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
