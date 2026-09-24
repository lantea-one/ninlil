using System.Xml.Serialization;
using Ninlil.Common.Serializer;
using Ninlil.Common.Serializer.Model;

namespace Ninlil.Common.Serializer.Tests.Model;

/// <summary>
///     This class contains unit tests for <see cref="SerializableDictionary{TKey, TValue}" />,
///     specifically its custom <see cref="IXmlSerializable" /> implementation. These tests
///     specifically lock in the fix for a bug where <c>WriteXml</c> embedded each value's full,
///     standalone XML document (including its own leading <c>&lt;?xml ...?&gt;</c> declaration)
///     directly inside the dictionary's own element -- an XML declaration is legal only as the
///     very first thing in an entire document, so any non-null value broke the round trip
///     entirely: <c>ReadXml</c> (via <see cref="System.Xml.XmlReader.ReadInnerXml" />) threw
///     trying to parse it back.
/// </summary>
public class SerializableDictionaryTests
{
    [Fact]
    public void RoundTrips_StringKeyedDictionary_ThroughXml()
    {
        var dictionary = new SerializableDictionary<string, string>
        {
            ["first"] = "one",
            ["second"] = "two",
        };

        string xml = XmlSerializer.Serialize(dictionary);
        var result = XmlSerializer.Deserialize<SerializableDictionary<string, string>>(xml);

        Assert.Equal(2, result.Count);
        Assert.Equal("one", result["first"]);
        Assert.Equal("two", result["second"]);
    }

    [Fact]
    public void RoundTrips_IntKeyedDictionary_ThroughXml()
    {
        var dictionary = new SerializableDictionary<int, string> { [1] = "a", [2] = "b" };

        string xml = XmlSerializer.Serialize(dictionary);
        var result = XmlSerializer.Deserialize<SerializableDictionary<int, string>>(xml);

        Assert.Equal(2, result.Count);
        Assert.Equal("a", result[1]);
        Assert.Equal("b", result[2]);
    }

    [Fact]
    public void RoundTrips_EmptyDictionary()
    {
        var dictionary = new SerializableDictionary<string, string>();

        string xml = XmlSerializer.Serialize(dictionary);
        var result = XmlSerializer.Deserialize<SerializableDictionary<string, string>>(xml);

        Assert.Empty(result);
    }

    [Fact]
    public void RoundTrips_NullValue()
    {
        var dictionary = new SerializableDictionary<string, string> { ["key"] = null };

        string xml = XmlSerializer.Serialize(dictionary);
        var result = XmlSerializer.Deserialize<SerializableDictionary<string, string>>(xml);

        Assert.True(result.ContainsKey("key"));
        Assert.Null(result["key"]);
    }

    [Fact]
    public void GetSchema_ReturnsNull()
    {
        var dictionary = new SerializableDictionary<string, string>();

        Assert.Null(dictionary.GetSchema());
    }

    [Fact]
    public void Constructor_FromExistingDictionary_CopiesEntries()
    {
        var source = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };

        var result = new SerializableDictionary<string, int>(source);

        Assert.Equal(1, result["a"]);
        Assert.Equal(2, result["b"]);
    }
}
