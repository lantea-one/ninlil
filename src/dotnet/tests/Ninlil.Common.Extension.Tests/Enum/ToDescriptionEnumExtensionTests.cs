using System.Runtime.Serialization;
using Ninlil.Common.Extension.Enum;
using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Enum;

/// <summary>
///     These tests convert a sample enum to descriptions with <see cref="ToDescriptionEnumExtension" />
///     and the default attribute checks: attributed and unattributed members, the generic and
///     non-generic overloads, an explicit value, and null receivers.
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
    ///     For a member with no attribute-driven description, <c>ToDescription</c> takes
    ///     <c>type.GetEnumDescriptions(enumValueName, attributeChecks).FirstOrDefault()</c>, which is
    ///     scoped to that one field, so the unattributed <see cref="SampleEnum.Second" /> returns
    ///     "Second", not another member's name.
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
    ///     Pins a known bug: the <c>ToDescription(Type, params attributeChecks[])</c> overload calls
    ///     <c>enumInstance.ToString()</c> before forwarding, so a null <see cref="System.Enum" />
    ///     throws <see cref="NullReferenceException" /> instead of returning <see langword="null" /> as
    ///     the three-argument overload does.
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
