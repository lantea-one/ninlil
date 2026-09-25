using System.ComponentModel;
using System.Runtime.Serialization;
using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Type;

/// <summary>
///     These tests list descriptions of a sample enum with <see cref="GetEnumDescriptionsTypeExtension" />
///     and the default attribute checks, for the whole type and for single members.
/// </summary>
public class GetEnumDescriptionsTypeExtensionTests
{
    /// <summary>
    ///     An enum with one <see cref="EnumMemberAttribute" /> member. The default checks also include
    ///     <see cref="DisplayNameAttribute" />, but its <see cref="AttributeUsageAttribute" /> does not
    ///     allow fields, so it cannot be applied to an enum member.
    /// </summary>
    private enum SampleDescribedEnum
    {
        [EnumMember(Value = "first-value")]
        First,

        Second,

        Third,
    }

    [Fact]
    public void GetEnumDescriptionsWithDefaultAttributeChecksIncludesEnumMemberValue()
    {
        IEnumerable<string> descriptions = typeof(SampleDescribedEnum).GetEnumDescriptions(
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks
        );

        Assert.Contains("first-value", descriptions);
    }

    [Fact]
    public void GetEnumDescriptionsAlwaysIncludesTheRawMemberNames()
    {
        IEnumerable<string> descriptions = typeof(SampleDescribedEnum).GetEnumDescriptions(
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks
        );

        Assert.Contains("First", descriptions);
        Assert.Contains("Second", descriptions);
        Assert.Contains("Third", descriptions);
    }

    [Fact]
    public void GetEnumDescriptionsReturnsEmptyForNonEnumType()
    {
        IEnumerable<string> descriptions = typeof(string).GetEnumDescriptions(
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks
        );

        Assert.Empty(descriptions);
    }

    [Fact]
    public void GetEnumDescriptionsDoesNotDuplicateIdenticalDescriptions()
    {
        IEnumerable<string> descriptions = typeof(SampleDescribedEnum)
            .GetEnumDescriptions(GetEnumDescriptionsTypeExtension.DefaultAttributeChecks)
            .ToList();

        Assert.Equal(descriptions.Distinct(StringComparer.Ordinal).Count(), descriptions.Count());
    }

    /// <summary>
    ///     The <c>fieldName</c> overload returns only that field's attribute-derived descriptions plus
    ///     its name, never another member's.
    /// </summary>
    [Fact]
    public void GetEnumDescriptionsForSpecificFieldNameIsScopedToThatFieldOnly()
    {
        IEnumerable<string> descriptions = typeof(SampleDescribedEnum)
            .GetEnumDescriptions(
                nameof(SampleDescribedEnum.First),
                GetEnumDescriptionsTypeExtension.DefaultAttributeChecks
            )
            .ToList();

        Assert.Contains("first-value", descriptions);
        Assert.Contains("First", descriptions);
        Assert.DoesNotContain("Second", descriptions);
        Assert.DoesNotContain("Third", descriptions);
    }

    /// <summary>
    ///     For an unattributed member, the <c>fieldName</c> overload returns only that field's name.
    /// </summary>
    [Fact]
    public void GetEnumDescriptionsForUnattributedFieldNameFallsBackToJustItsOwnName()
    {
        IEnumerable<string> descriptions = typeof(SampleDescribedEnum)
            .GetEnumDescriptions(
                nameof(SampleDescribedEnum.Second),
                GetEnumDescriptionsTypeExtension.DefaultAttributeChecks
            )
            .ToList();

        Assert.Equal(["Second"], descriptions);
    }

    [Fact]
    public void GetEnumDescriptionsGenericMatchesNonGenericOverload()
    {
        IEnumerable<string> generic = typeof(SampleDescribedEnum)
            .GetEnumDescriptions<SampleDescribedEnum>(
                GetEnumDescriptionsTypeExtension.DefaultAttributeChecks
            )
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();

        IEnumerable<string> nonGeneric = typeof(SampleDescribedEnum)
            .GetEnumDescriptions(GetEnumDescriptionsTypeExtension.DefaultAttributeChecks)
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(nonGeneric, generic);
    }

    [Fact]
    public void GetEnumDescriptionsWithParamsOverloadMatchesEnumerableOverload()
    {
        IEnumerable<string> viaParams = typeof(SampleDescribedEnum).GetEnumDescriptions(
            GetEnumDescriptionsTypeExtension.DefaultAttributeChecks.ToArray()
        );

        Assert.Contains("first-value", viaParams);
    }
}
