using Ninlil.Common.Serializer;
using Ninlil.Common.Serializer.Model;
using YamlDotNet.Core;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     These tests cover <see cref="Serializer.Converter.StructuredStringYamlConverter{TObject}" />
///     through <see cref="TimeStringModel" />, with sibling properties to catch parser-positioning
///     mistakes (see <see cref="IpAddressYamlConverterTests" />). Text that does not parse and
///     non-scalar nodes are refused with a <see cref="YamlException" />; empty, blank and plain null
///     scalars read as null.
/// </summary>
public class StructuredStringYamlConverterTests
{
    private class Wrapper
    {
        public TimeStringModel First { get; set; }
        public TimeStringModel Second { get; set; }
    }

    [Fact]
    public void RoundTrips_TwoSiblingValues()
    {
        var wrapper = new Wrapper
        {
            First = TimeStringModel.FromString("5m"),
            Second = TimeStringModel.FromString("1h"),
        };

        string yaml = YamlSerializer.Serialize(wrapper);
        Wrapper result = YamlSerializer.Deserialize<Wrapper>(yaml);

        Assert.Equal("5m", result.First.ToString());
        Assert.Equal("1h", result.Second.ToString());
    }

    [Fact]
    public void Deserialize_EmptyString_ReturnsNull()
    {
        Wrapper result = YamlSerializer.Deserialize<Wrapper>("first: \"\"\n");

        Assert.Null(result.First);
    }

    [Theory]
    [InlineData("first: ~\n")]
    [InlineData("first: null\n")]
    [InlineData("first: Null\n")]
    [InlineData("first: NULL\n")]
    [InlineData("first:\n")]
    [InlineData("first: ''\n")]
    [InlineData("first: '   '\n")]
    public void Deserialize_BlankOrPlainNullScalar_ReturnsNull(string yaml)
    {
        Wrapper result = YamlSerializer.Deserialize<Wrapper>(yaml);

        Assert.Null(result.First);
    }

    [Fact]
    public void Deserialize_TextThatDoesNotParse_ThrowsYamlExceptionWithTheScalarMarks()
    {
        YamlException exception = Assert.Throws<YamlException>(() =>
            YamlSerializer.Deserialize<Wrapper>("first: not a time\n")
        );

        Assert.Contains("not a time", exception.Message);
        Assert.Equal(0, exception.Start.Line - 1);
        Assert.True(exception.End.Column > exception.Start.Column);
    }

    [Fact]
    public void Deserialize_AQuotedNullWord_IsNotANullAndThrows()
    {
        // Only the plain forms are YAML nulls; a quoted word is text and must parse as a value.
        Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Wrapper>("first: 'null'\n"));
    }

    [Fact]
    public void Deserialize_AMappingNode_ThrowsYamlExceptionWithTheNodeMarks()
    {
        YamlException exception = Assert.Throws<YamlException>(() =>
            YamlSerializer.Deserialize<Wrapper>("first: {a: 1}\nsecond: 1h\n")
        );

        Assert.Equal(0, exception.Start.Line - 1);
    }

    [Fact]
    public void Deserialize_ASequenceNode_ThrowsYamlException()
    {
        Assert.Throws<YamlException>(() =>
            YamlSerializer.Deserialize<Wrapper>("first: [a, b]\nsecond: 1h\n")
        );
    }
}
