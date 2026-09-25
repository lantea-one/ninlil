using System.Runtime.Serialization;
using Ninlil.Common.Extension.Converter;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Converter;

/// <summary>
///     These tests register <see cref="EnumDescriptionYamlConverter" /> with YamlDotNet's
///     <c>SerializerBuilder</c>/<c>DeserializerBuilder</c> (<c>WithTypeConverter</c>) and round-trip
///     a sample enum. Nothing else in the codebase references this converter, so these tests are
///     its only coverage.
/// </summary>
/// <remarks>
///     <c>WriteYaml</c> must pass the concrete enum <c>type</c> to <c>ToDescription</c>; calling the
///     generic overload on a <see cref="System.Enum" />-typed value finds no fields and falls back to
///     <see cref="object.ToString" />. <c>ReadYaml</c> must read the current scalar before calling
///     <c>MoveNext()</c>, or it skips the value.
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
    ///     A minimal wrapper class, because YamlDotNet round-trips a scalar property more simply than
    ///     a bare top-level enum.
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
    ///     The converter writes the attribute-driven description for an attributed member instead of
    ///     the member name.
    /// </summary>
    [Fact]
    public void SerializeEmitsTheAttributeDrivenDescriptionForAnAttributedMember()
    {
        string yaml = BuildSerializer().Serialize(new Wrapper { Value = SampleEnum.First });

        Assert.Contains("first-value", yaml);
        Assert.DoesNotContain("First", yaml);
    }

    /// <summary>
    ///     For an unattributed member, serialization falls back to that member's name (through
    ///     <c>ToDescription</c>'s fallback), not another member's.
    /// </summary>
    [Fact]
    public void SerializeFallsBackToTheRawMemberNameForAnUnattributedMember()
    {
        string yaml = BuildSerializer().Serialize(new Wrapper { Value = SampleEnum.Second });

        Assert.Contains("Second", yaml);
    }

    /// <summary>
    ///     <c>ReadYaml</c> reads <c>parser.Current</c> before calling <c>MoveNext()</c>, so both the
    ///     attribute-driven description and a plain member name deserialize to the right member.
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
