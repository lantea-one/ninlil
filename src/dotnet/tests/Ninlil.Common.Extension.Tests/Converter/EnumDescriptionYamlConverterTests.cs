using System.Runtime.Serialization;
using Ninlil.Common.Extension.Converter;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Converter;

/// <summary>
///     This class tests <see cref="EnumDescriptionYamlConverter" /> through the standard, documented
///     YamlDotNet usage pattern (<c>SerializerBuilder</c>/<c>DeserializerBuilder</c>.<c>WithTypeConverter</c>)
///     -- the only way this converter is meant to be consumed. It is not currently referenced anywhere
///     else in the codebase, so these tests are this converter's only real exercise.
/// </summary>
/// <remarks>
///     <c>WriteYaml</c> now explicitly passes the real, concrete enum <c>type</c> YamlDotNet hands it to
///     <c>ToDescription</c>, rather than calling <c>TryToDescription</c> off a <c>value as System.Enum</c>
///     -typed receiver (whose compile-time type is the abstract <see cref="System.Enum" /> base class --
///     that used to resolve the generic overload with its type parameter bound to
///     <see cref="System.Enum" /> itself instead of the real enum type, so the underlying lookup found no
///     matching field/type and silently fell back to the enum's raw <see cref="object.ToString" />).
///     <c>ReadYaml</c> now reads the parser's already-current scalar node first and only calls
///     <c>MoveNext()</c> afterward to consume it, matching the standard <c>IYamlTypeConverter</c> contract
///     (an earlier version called <c>MoveNext()</c> before inspecting <c>Current</c>, skipping straight
///     past the actual scalar node).
/// </remarks>
public class EnumDescriptionYamlConverterTests
{
    /// <summary>
    ///     A sample enum decorated with <see cref="EnumMemberAttribute" /> on one member, used to
    ///     round-trip through YAML serialization.
    /// </summary>
    public enum SampleEnum
    {
        [EnumMember(Value = "first-value")]
        First,

        Second,
    }

    /// <summary>
    ///     A minimal wrapper class, since YamlDotNet's default document root handling is simplest to
    ///     exercise against a scalar-valued property rather than a bare top-level enum.
    /// </summary>
    public class Wrapper
    {
        public SampleEnum Value { get; set; }
    }

    private static ISerializer BuildSerializer() =>
        new SerializerBuilder().WithTypeConverter(new EnumDescriptionYamlConverter()).Build();

    private static IDeserializer BuildDeserializer() =>
        new DeserializerBuilder().WithTypeConverter(new EnumDescriptionYamlConverter()).Build();

    [Fact]
    public void AcceptsReturnsTrueForAnyEnumType()
    {
        Assert.True(new EnumDescriptionYamlConverter().Accepts(typeof(SampleEnum)));
    }

    [Fact]
    public void AcceptsReturnsFalseForANonEnumType()
    {
        Assert.False(new EnumDescriptionYamlConverter().Accepts(typeof(string)));
    }

    /// <summary>
    ///     The registered custom converter now correctly emits the attribute-driven description for an
    ///     attributed member instead of the raw member name.
    /// </summary>
    [Fact]
    public void SerializeEmitsTheAttributeDrivenDescriptionForAnAttributedMember()
    {
        string yaml = BuildSerializer().Serialize(new Wrapper { Value = SampleEnum.First });

        Assert.Contains("first-value", yaml);
        Assert.DoesNotContain("First", yaml);
    }

    /// <summary>
    ///     For an unattributed member, serialization falls back to that member's own raw name (via
    ///     <c>ToDescription</c>'s own fallback), not another member's.
    /// </summary>
    [Fact]
    public void SerializeFallsBackToTheRawMemberNameForAnUnattributedMember()
    {
        string yaml = BuildSerializer().Serialize(new Wrapper { Value = SampleEnum.Second });

        Assert.Contains("Second", yaml);
    }

    /// <summary>
    ///     <c>ReadYaml</c> now correctly locates the scalar node (reading <c>parser.Current</c> before
    ///     calling <c>MoveNext()</c>), so deserializing round-trips both the attribute-driven description
    ///     and a raw member name back to the correct enum member.
    /// </summary>
    [Fact]
    public void DeserializeResolvesBothTheAttributeValueAndTheRawMemberNameToTheCorrectMember()
    {
        Wrapper fromAttributeValue = BuildDeserializer()
            .Deserialize<Wrapper>("Value: first-value\n");
        Wrapper fromRawSecondName = BuildDeserializer().Deserialize<Wrapper>("Value: Second\n");

        Assert.Equal(SampleEnum.First, fromAttributeValue.Value);
        Assert.Equal(SampleEnum.Second, fromRawSecondName.Value);
    }
}
