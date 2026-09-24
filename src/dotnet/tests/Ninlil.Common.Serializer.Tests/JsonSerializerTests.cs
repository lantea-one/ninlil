using System.Text.Json;
using System.Text.Json.Serialization;
using Ninlil.Common.Serializer;

namespace Ninlil.Common.Serializer.Tests;

/// <summary>
///     This class contains unit tests for the core <see cref="JsonSerializer" /> static wrapper,
///     including the documented PascalCase-internal/no-naming-policy behavior, and two fixes made
///     to <see cref="JsonSerializer.GetOptions" />:
///     <list type="bullet">
///         <item>
///             It now returns an already-read-only <see cref="JsonSerializerOptions" /> instance
///             verbatim instead of attempting to mutate it (which throws once an options instance
///             has been used for a real (de)serialization operation).
///         </item>
///         <item>
///             It now skips adding a converter type that's already present on the target options
///             instance, instead of unconditionally appending the library's whole default
///             converter list every call (which piled up duplicate converter instances on repeated
///             calls against the same still-mutable options instance).
///         </item>
///     </list>
/// </summary>
public class JsonSerializerTests
{
    private interface IHasName
    {
        string Name { get; }
    }

    private class Concrete : IHasName
    {
        public string Name { get; set; }
        public int OnlyOnConcrete { get; set; }
    }

    private class Simple
    {
        public string Value { get; set; }
    }

    [Fact]
    public void Serialize_Deserialize_RoundTrips()
    {
        var payload = new Simple { Value = "hello" };

        string json = JsonSerializer.Serialize(payload);
        Simple result = JsonSerializer.Deserialize<Simple>(json);

        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public void Serialize_UsesPascalCase_NoNamingPolicyApplied()
    {
        // Per this project's documented convention, internal storage is always PascalCase --
        // JsonSerializer sets no PropertyNamingPolicy, so property names round-trip verbatim.
        string json = JsonSerializer.Serialize(new Simple { Value = "x" });

        Assert.Contains("\"Value\":\"x\"", json);
    }

    [Fact]
    public void Deserialize_NullOrEmptyJson_ReturnsDefault()
    {
        Assert.Null(JsonSerializer.Deserialize<Simple>(null));
        Assert.Null(JsonSerializer.Deserialize<Simple>(""));
        Assert.Null(JsonSerializer.Deserialize<Simple>("   "));
    }

    [Fact]
    public void Deserialize_NonGeneric_NullOrEmptyJson_ReturnsNull()
    {
        Assert.Null(JsonSerializer.Deserialize(typeof(Simple), null));
        Assert.Null(JsonSerializer.Deserialize(typeof(Simple), ""));
    }

    [Fact]
    public void Serialize_NullPayload_ReturnsNull()
    {
        Assert.Null(JsonSerializer.Serialize((object)null));
    }

    [Fact]
    public void SerializePretty_ProducesIndentedOutput()
    {
        string compact = JsonSerializer.Serialize(new Simple { Value = "x" });
        string pretty = JsonSerializer.SerializePretty(new Simple { Value = "x" });

        Assert.DoesNotContain("\n", compact);
        Assert.Contains("\n", pretty);
    }

    [Fact]
    public void TryDeserialize_ValidJson_ReturnsTrue()
    {
        bool success = JsonSerializer.TryDeserialize("""{"Value":"x"}""", out Simple result);

        Assert.True(success);
        Assert.Equal("x", result.Value);
    }

    [Fact]
    public void TryDeserialize_MalformedJson_ReturnsFalse()
    {
        bool success = JsonSerializer.TryDeserialize("not json", out Simple result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void TrySerialize_ValidPayload_ReturnsTrue()
    {
        bool success = JsonSerializer.TrySerialize(new Simple { Value = "x" }, out string json);

        Assert.True(success);
        Assert.Contains("\"Value\":\"x\"", json);
    }

    [Fact]
    public void GetOptions_ReadOnlyInstance_IsReturnedVerbatim_NotMutated()
    {
        // Lock an options instance by using it for a real serialization first.
        var options = new JsonSerializerOptions();
        _ = System.Text.Json.JsonSerializer.Serialize(new Simple(), options);
        Assert.True(options.IsReadOnly);

        int converterCountBefore = options.Converters.Count;

        // Before the fix, this would throw InvalidOperationException trying to mutate a locked
        // options instance (setting WriteIndented, adding converters).
        JsonSerializerOptions result = JsonSerializer.GetOptions(options);

        Assert.Same(options, result);
        Assert.Equal(converterCountBefore, result.Converters.Count);
    }

    [Fact]
    public void GetOptions_CalledRepeatedly_OnSameMutableInstance_DoesNotDuplicateConverters()
    {
        var options = new JsonSerializerOptions();

        JsonSerializer.GetOptions(options);
        JsonSerializer.GetOptions(options);
        JsonSerializer.GetOptions(options);

        // Each converter TYPE in the library's default set must appear at most once, regardless
        // of how many times GetOptions() is called against the same still-mutable instance.
        var converterTypeCounts = options
            .Converters.GroupBy(c => c.GetType())
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.All(converterTypeCounts, kvp => Assert.Equal(1, kvp.Value));
    }

    [Fact]
    public void GetOptions_NoArguments_AppliesLibraryDefaults()
    {
        JsonSerializerOptions options = JsonSerializer.GetOptions();

        Assert.True(options.AllowTrailingCommas);
        Assert.True(options.PropertyNameCaseInsensitive);
        Assert.Equal(JsonCommentHandling.Skip, options.ReadCommentHandling);
        Assert.False(options.WriteIndented);
        Assert.NotEmpty(options.Converters);
    }

    [Fact]
    public void GetOptions_PrettyFlag_SetsWriteIndented()
    {
        JsonSerializerOptions options = JsonSerializer.GetOptions(pretty: true);

        Assert.True(options.WriteIndented);
    }

    [Fact]
    public void AddConverter_ByInstance_DoesNotDuplicate()
    {
        int before = JsonSerializer.Converters.Count;

        var converter = new JsonStringEnumConverter();
        JsonSerializer.AddConverter(converter);
        JsonSerializer.AddConverter(converter);

        // A converter of a type that's already registered (JsonStringEnumConverter is already in
        // the library's default set) must not be added again.
        Assert.Equal(before, JsonSerializer.Converters.Count);
    }

    [Fact]
    public void Serialize_WithExplicitConcreteType_SerializesConcreteMembers()
    {
        // This documents the current, real behavior behind the interface-typed-payload trap: the non-generic
        // Serialize(object payload) overload determines the serialization type from the runtime
        // object via payload.GetType() (not the compile-time static type), so it is NOT
        // susceptible to the "interface trap" -- it always sees the concrete shape.
        IHasName asInterface = new Concrete { Name = "n", OnlyOnConcrete = 42 };

        string json = JsonSerializer.Serialize((object)asInterface);

        Assert.Contains("\"OnlyOnConcrete\":42", json);
    }

    [Fact]
    public void SerializeGeneric_WithInterfaceTypeArgument_SerializesOnlyInterfaceMembers()
    {
        // In contrast, the generic Serialize<TSource>(TSource payload) overload passes
        // typeof(TSource) through explicitly -- if TSource is inferred/specified as an interface
        // type, System.Text.Json serializes only that interface's own shape, exactly the
        // interface-typed-payload trap. This is inherent to how the generic overload is
        // implemented (it mirrors System.Text.Json's own generic API), not something this
        // wrapper can silently correct without changing its documented, type-driven contract --
        // this test exists to make the real, current behavior explicit rather than assumed.
        IHasName asInterface = new Concrete { Name = "n", OnlyOnConcrete = 42 };

        string json = JsonSerializer.Serialize(asInterface);

        Assert.Contains("\"Name\":\"n\"", json);
        Assert.DoesNotContain("OnlyOnConcrete", json);
    }
}
