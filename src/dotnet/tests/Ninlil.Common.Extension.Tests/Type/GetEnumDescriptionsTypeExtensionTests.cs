using System.ComponentModel;
using System.Runtime.Serialization;
using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Type;

/// <summary>
///     This class tests <see cref="GetEnumDescriptionsTypeExtension" />.
/// </summary>
public class GetEnumDescriptionsTypeExtensionTests
{
    /// <summary>
    ///     An enum decorated with the same description-carrying attribute the production code checks
    ///     for by default (<see cref="EnumMemberAttribute" />; <see cref="DisplayNameAttribute" /> is
    ///     also in the default attribute-check list, but cannot itself be applied to an enum member --
    ///     its own <see cref="AttributeUsageAttribute" /> only allows class/method/property/indexer/event
    ///     targets, not fields, so it can never actually fire against an enum value in practice).
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
    ///     The <c>fieldName</c> overload scopes its result to just that one field: its attribute-derived
    ///     description(s), plus the field's own raw name as a fallback -- never another member's name or
    ///     value.
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
    ///     For an unattributed member, the <c>fieldName</c> overload's result falls back to just that
    ///     field's own raw name -- not any other member's, and not every member concatenated together.
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
