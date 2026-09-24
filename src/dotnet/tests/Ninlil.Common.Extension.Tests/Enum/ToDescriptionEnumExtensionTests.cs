using System.Runtime.Serialization;
using Ninlil.Common.Extension.Enum;
using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Enum;

/// <summary>
///     This class tests <see cref="ToDescriptionEnumExtension" />.
/// </summary>
public class ToDescriptionEnumExtensionTests
{
    /// <summary>
    ///     A sample enum decorated with <see cref="EnumMemberAttribute" /> on one member, used to
    ///     exercise both the attribute-driven and raw-name-fallback description paths.
    /// </summary>
    private enum SampleEnum
    {
        [EnumMember(Value = "first-value")]
        First,

        Second,
    }

    [Fact]
    public void ToDescriptionOnAttributedMemberReturnsTheAttributeValue()
    {
        string result = SampleEnum.First.ToDescription(
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks
        );

        Assert.Equal("first-value", result);
    }

    /// <summary>
    ///     For a member with no matching attribute-driven description, the production code falls back to
    ///     <c>type.GetEnumDescriptions(enumValueName, attributeChecks).FirstOrDefault()</c> -- which is
    ///     now correctly scoped to just the requested field, so calling <c>ToDescription()</c> on the
    ///     unattributed <see cref="SampleEnum.Second" /> member returns its own name, "Second", not
    ///     another member's.
    /// </summary>
    [Fact]
    public void ToDescriptionOnUnattributedMemberReturnsItsOwnName()
    {
        string result = SampleEnum.Second.ToDescription(
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks
        );

        Assert.Equal("Second", result);
    }

    /// <summary>
    ///     Documents a real bug: the <c>ToDescription(Type, params attributeChecks[])</c> overload
    ///     forwards to <c>enumInstance.ToDescription(type, enumInstance.ToString(), attributeChecks)</c>
    ///     without first checking for a null receiver, so calling it on a null
    ///     <see cref="System.Enum" /> reference throws a <see cref="NullReferenceException" /> instead of
    ///     returning <see langword="null" /> the way the 3-argument overload it forwards to explicitly
    ///     does when called directly.
    /// </summary>
    [Fact]
    public void ToDescriptionWithTypeAndParamsOverloadThrowsOnNullEnumInsteadOfReturningNull()
    {
        System.Enum instance = null;

        Assert.Throws<NullReferenceException>(() =>
            instance.ToDescription(
                typeof(SampleEnum),
                GetEnumDescriptionsTypeExtension.DefaultAttributeChecks.ToArray()
            )
        );
    }

    [Fact]
    public void ToDescriptionGenericMatchesNonGenericOverload()
    {
        string generic = SampleEnum.First.ToDescription(
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks
        );
        string nonGeneric = ((System.Enum)SampleEnum.First).ToDescription(
            typeof(SampleEnum),
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks.ToArray()
        );

        Assert.Equal(nonGeneric, generic);
    }

    [Fact]
    public void ToDescriptionWithExplicitValueConvertsThatValueRatherThanTheInstance()
    {
        string result = SampleEnum.Second.ToDescription(
            "First",
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks.ToArray()
        );

        Assert.Equal("first-value", result);
    }

    [Fact]
    public void TryToDescriptionOnValidInstanceReturnsTrueAndSetsDescription()
    {
        bool success = SampleEnum.First.TryToDescription(
            out string description,
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks
        );

        Assert.True(success);
        Assert.Equal("first-value", description);
    }

    [Fact]
    public void TryToDescriptionOnNullEnumReturnsFalse()
    {
        System.Enum instance = null;

        bool success = instance.TryToDescription(
            typeof(SampleEnum),
            out string description,
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks.ToArray()
        );

        Assert.False(success);
        Assert.Null(description);
    }
}
