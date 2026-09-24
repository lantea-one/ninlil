using System.Text.Json.Serialization;
using Ninlil.Common.Serializer;
using Ninlil.Common.Serializer.Attribute;
using Ninlil.Common.Serializer.Converter;

namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     This class contains unit tests for <see cref="ListMapJsonConverter{TItem}" />, the
///     reflection-based converter that treats a JSON array as a "list map" -- one property of each
///     item, marked with <see cref="ListMapKeyAttribute" />, is its unique key. The converter is
///     always applied via a <see cref="JsonConverterAttribute" /> on the specific
///     <see cref="List{TItem}" />-typed property it targets (never registered globally), so every
///     test here round-trips through a small wrapper class carrying that attribute.
///     These tests specifically lock in two fixes made to this converter:
///     <list type="bullet">
///         <item>
///             The item type it binds to changed from <see cref="IEnumerable{TItem}" /> to
///             <see cref="List{TItem}" />, and its <c>Write</c> method now emits a JSON array of
///             per-item objects (the exact shape <c>Read</c> parses back) instead of a JSON object
///             keyed by each item's key value -- the two were previously incompatible shapes, so a
///             value this converter wrote could never be read back by the same converter.
///         </item>
///         <item>
///             It calls back into this project's own <see cref="JsonSerializer" /> wrapper while
///             already inside another (de)serialization call, which used to throw because
///             <c>JsonSerializerOptions</c> becomes read-only once used -- see
///             <see cref="RoundTrips_AsNestedPropertyOfAContainer_ThroughAlreadyLockedOptions" />.
///         </item>
///     </list>
/// </summary>
public class ListMapJsonConverterTests
{
    /// <summary>
    ///     This item type's key property is NOT ignored, so it round-trips as an ordinary property
    ///     in addition to being (redundantly) re-injected by the converter.
    /// </summary>
    private class VisibleKeyItem
    {
        [ListMapKey]
        public string Name { get; set; }

        public int Value { get; set; }
    }

    /// <summary>
    ///     This item type's key property IS ignored by ordinary JSON serialization, so it can only
    ///     round-trip correctly via the converter's explicit reflection-based re-injection.
    /// </summary>
    private class HiddenKeyItem
    {
        [ListMapKey]
        [JsonIgnore]
        public string Id { get; set; }

        public string Description { get; set; }
    }

    /// <summary>
    ///     A container carrying the converter attribute on the property itself, as this converter
    ///     is always meant to be applied -- never registered globally.
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

        // The write shape must be a JSON array (not an object keyed by the item's key value) --
        // this is what makes it symmetric with Read().
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

        // Even though Id is [JsonIgnore]'d, it must still appear in the written JSON under its own
        // property name -- the converter explicitly (re-)injects it.
        Assert.Contains("\"Id\":\"id-1\"", json);

        HiddenKeyContainer result = JsonSerializer.Deserialize<HiddenKeyContainer>(json);

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, i => i.Id == "id-1" && i.Description == "first");
        Assert.Contains(result.Items, i => i.Id == "id-2" && i.Description == "second");
    }

    [Fact]
    public void RoundTrips_AsNestedPropertyOfAContainer_ThroughAlreadyLockedOptions()
    {
        // Serializing/deserializing a container object (rather than the list directly) is what
        // hands the converter an options instance that the framework has already locked
        // read-only -- this is the exact scenario the JsonSerializer.GetOptions() read-only fix
        // covers. Prior to that fix, this call would throw InvalidOperationException, since the
        // converter's own calls back into JsonSerializer.Serialize/Deserialize try to add
        // converters to (and otherwise mutate) that same locked options instance.
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
        // System.Text.Json never invokes a JsonConverter<T>'s Write() for a null reference-typed
        // value unless the converter opts in via HandleNull -- this converter doesn't, so a null
        // list serializes as JSON null directly, without the converter running at all. This test
        // exists to document that (and to guard against ever changing HandleNull without also
        // updating this expectation).
        var container = new VisibleKeyContainer { Items = null };

        string json = JsonSerializer.Serialize(container);

        Assert.Contains("\"Items\":null", json);
    }
}
