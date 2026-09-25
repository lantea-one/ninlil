using System.Runtime.Serialization;
using System.Text.Json;
using Ninlil.Common.Extension.Converter;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Converter;

/// <summary>
///     These tests round-trip a sample enum through System.Text.Json with
///     <see cref="EnumDescriptionJsonConverter{TEnum}" />: attributed values, raw member names and
///     unattributed members.
/// </summary>
public class EnumDescriptionJsonConverterTests
{
    /// <summary>
    ///     A sample enum decorated with <see cref="EnumMemberAttribute" /> on one member, used to
    ///     round-trip through JSON serialization.
    /// </summary>
    private enum SampleEnum
    {
        [EnumMember(Value = "first-value")]
        First,

        Second,
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new EnumDescriptionJsonConverter<SampleEnum>() },
    };

    [Fact]
    public void WriteOnAttributedMemberSerializesTheAttributeValue()
    {
        string json = JsonSerializer.Serialize(SampleEnum.First, Options);

        Assert.Equal("\"first-value\"", json);
    }

    [Fact]
    public void ReadOnAttributedValueDeserializesToTheAttributedMember()
    {
        SampleEnum result = JsonSerializer.Deserialize<SampleEnum>("\"first-value\"", Options);

        Assert.Equal(SampleEnum.First, result);
    }

    [Fact]
    public void ReadOnRawMemberNameDeserializesToThatMember()
    {
        SampleEnum result = JsonSerializer.Deserialize<SampleEnum>("\"Second\"", Options);

        Assert.Equal(SampleEnum.Second, result);
    }

    /// <summary>
    ///     <c>Write</c> calls <c>value.ToDescription(...)</c>, which falls back to the member's own
    ///     name (see <c>ToDescriptionEnumExtensionTests</c>). An unattributed member that is not the
    ///     first declared must be written as its name, not the first member's.
    /// </summary>
    [Fact]
    public void WriteOnUnattributedMemberSerializesItsOwnName()
    {
        string json = JsonSerializer.Serialize(SampleEnum.Second, Options);

        Assert.Equal("\"Second\"", json);
    }
}
