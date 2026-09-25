using System.Xml.Serialization;
using Ninlil.Common.Serializer;
using Ninlil.Common.Serializer.Model;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Model;

/// <summary>
///     These tests round-trip <see cref="SerializableDictionary{TKey, TValue}" /> through its
///     <see cref="IXmlSerializable" /> implementation. Each value must be written as a fragment
///     without an <c>&lt;?xml ...?&gt;</c> declaration, which is only legal at the start of a
///     document; otherwise <c>ReadXml</c> (through <see cref="System.Xml.XmlReader.ReadInnerXml" />)
///     cannot parse it back.
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
