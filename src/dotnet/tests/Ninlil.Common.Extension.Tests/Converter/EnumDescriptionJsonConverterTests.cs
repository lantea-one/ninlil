using System.Runtime.Serialization;
using System.Text.Json;
using Ninlil.Common.Extension.Converter;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Converter;

/// <summary>
///     This class tests <see cref="EnumDescriptionJsonConverter{TEnum}" />.
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
    ///     The converter's <c>Write</c> implementation calls <c>value.ToDescription(...)</c>, which is now
    ///     correctly scoped per-member (see <c>ToDescriptionEnumExtensionTests</c>), so serializing an
    ///     unattributed enum member other than the first-declared one writes its own JSON string, not the
    ///     first-declared member's.
    /// </summary>
    [Fact]
    public void WriteOnUnattributedMemberSerializesItsOwnName()
    {
        string json = JsonSerializer.Serialize(SampleEnum.Second, Options);

        Assert.Equal("\"Second\"", json);
    }
}
