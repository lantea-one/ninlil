using System.Text.Json.Serialization;
using Ninlil.Common.Serializer;
using Ninlil.Common.Serializer.Attribute;
using Ninlil.Common.Serializer.Converter;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     These tests cover <see cref="ListMapJsonConverter{TItem}" />, which treats a JSON array as a
///     "list map" whose items are keyed by the property marked with <see cref="ListMapKeyAttribute" />.
///     The converter is applied with a <see cref="JsonConverterAttribute" /> on a
///     <see cref="List{TItem}" /> property, never globally, so every test round-trips through a small
///     wrapper class carrying that attribute. They check that <c>Write</c> emits the array shape
///     <c>Read</c> parses back, that a <c>[JsonIgnore]</c> key still round-trips, and that the
///     converter works when nested inside another serialization call, where the options are
///     already read-only (see <see cref="RoundTrips_AsNestedPropertyOfAContainer_ThroughAlreadyLockedOptions" />).
/// </summary>
public class ListMapJsonConverterTests
{
    /// <summary>
    ///     This item type's key property is not ignored, so it round-trips as an ordinary property
    ///     as well as being added again by the converter.
    /// </summary>
    private class VisibleKeyItem
    {
        [ListMapKey]
        public string Name { get; set; }

        public int Value { get; set; }
    }

    /// <summary>
    ///     This item type's key property is ignored by ordinary JSON serialization, so it only
    ///     round-trips because the converter reads and writes it explicitly.
    /// </summary>
    private class HiddenKeyItem
    {
        [ListMapKey]
        [JsonIgnore]
        public string Id { get; set; }

        public string Description { get; set; }
    }

    /// <summary>
    ///     A container carrying the converter attribute on the property, which is how this converter
    ///     is applied.
    /// </summary>
    private class VisibleKeyContainer
    {
        [JsonConverter(typeof(ListMapJsonConverter<VisibleKeyItem>))]
        public List<VisibleKeyItem> Items { get; set; }
    }

    private class HiddenKeyContainer
    {
        [JsonConverter(typeof(ListMapJsonConverter<HiddenKeyItem>))]
        public List<HiddenKeyItem> Items { get; set; }
    }

    [Fact]
    public void RoundTrips_VisibleKeyItems_AsJsonArray()
    {
        var container = new VisibleKeyContainer
        {
            Items = [new() { Name = "a", Value = 1 }, new() { Name = "b", Value = 2 }],
        };

        string json = JsonSerializer.Serialize(container);

        // The written shape must be a JSON array (not an object keyed by each item's key), the
        // shape Read() expects.
        Assert.Contains("\"Items\":[{", json);

        VisibleKeyContainer result = JsonSerializer.Deserialize<VisibleKeyContainer>(json);

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, i => i.Name == "a" && i.Value == 1);
        Assert.Contains(result.Items, i => i.Name == "b" && i.Value == 2);
    }

    [Fact]
    public void RoundTrips_HiddenKeyItems_ThroughReflectionReinjection()
    {
        var container = new HiddenKeyContainer
        {
            Items =
            [
                new() { Id = "id-1", Description = "first" },
                new() { Id = "id-2", Description = "second" },
            ],
        };

        string json = JsonSerializer.Serialize(container);

        // Id is [JsonIgnore]'d but must still appear in the written JSON under its property
        // name, because the converter adds it.
        Assert.Contains("\"Id\":\"id-1\"", json);

        HiddenKeyContainer result = JsonSerializer.Deserialize<HiddenKeyContainer>(json);

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, i => i.Id == "id-1" && i.Description == "first");
        Assert.Contains(result.Items, i => i.Id == "id-2" && i.Description == "second");
    }

    [Fact]
    public void RoundTrips_AsNestedPropertyOfAContainer_ThroughAlreadyLockedOptions()
    {
        // Serializing a container (rather than the list itself) hands the converter options the
        // framework has already made read-only. JsonSerializer.GetOptions() must return those
        // unchanged; changing them would throw InvalidOperationException.
        var container = new HiddenKeyContainer
        {
            Items = [new() { Id = "x", Description = "nested" }],
        };

        string json = JsonSerializer.Serialize(container);
        HiddenKeyContainer result = JsonSerializer.Deserialize<HiddenKeyContainer>(json);

        Assert.Single(result.Items);
        Assert.Equal("x", result.Items[0].Id);
        Assert.Equal("nested", result.Items[0].Description);
    }

    [Fact]
    public void Read_EmptyArray_ReturnsEmptyList()
    {
        VisibleKeyContainer result = JsonSerializer.Deserialize<VisibleKeyContainer>(
            """{"Items":[]}"""
        );

        Assert.NotNull(result.Items);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void Read_NonArrayJson_ReturnsEmptyList()
    {
        VisibleKeyContainer result = JsonSerializer.Deserialize<VisibleKeyContainer>(
            """{"Items":{}}"""
        );

        Assert.NotNull(result.Items);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void Read_ElementMissingKey_IsSkipped()
    {
        VisibleKeyContainer result = JsonSerializer.Deserialize<VisibleKeyContainer>(
            """{"Items":[{"Value":1}]}"""
        );

        Assert.Empty(result.Items);
    }

    [Fact]
    public void Read_NullList_WhenPropertyOmitted_StaysNull()
    {
        VisibleKeyContainer result = JsonSerializer.Deserialize<VisibleKeyContainer>("{}");

        Assert.Null(result.Items);
    }

    [Fact]
    public void Write_NullList_WritesJsonNull_NotThrow()
    {
        // System.Text.Json does not call Write() for a null reference value unless the converter
        // sets HandleNull, which this one does not, so a null list is written as JSON null without
        // the converter running. Changing HandleNull must update this expectation.
        var container = new VisibleKeyContainer { Items = null };

        string json = JsonSerializer.Serialize(container);

        Assert.Contains("\"Items\":null", json);
    }
}
