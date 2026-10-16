using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ninlil.Common.Serializer;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests;

/// <summary>
///     These tests cover the <see cref="JsonSerializer" /> static wrapper: round trips, null and
///     malformed input, property names kept as declared (no naming policy), and
///     <see cref="JsonSerializer.GetOptions" />, which must return read-only options unchanged
///     and must not add a converter type that is already present. They change no process-wide state
///     and run in parallel with other classes; the tests that change the shared defaults or the
///     shared converter list are in <see cref="JsonSerializerProcessWideTests" />.
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

    private class Addressed
    {
        public IPAddress Address { get; set; }
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
        // JsonSerializer sets no PropertyNamingPolicy, so property names round-trip as declared
        // (PascalCase).
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
        // Make the options read-only by using them for a serialization first.
        var options = new JsonSerializerOptions();
        _ = System.Text.Json.JsonSerializer.Serialize(new Simple(), options);
        Assert.True(options.IsReadOnly);

        int converterCountBefore = options.Converters.Count;

        // Changing read-only options (WriteIndented, converters) would throw
        // InvalidOperationException, so GetOptions must return them untouched.
        JsonSerializerOptions result = JsonSerializer.GetOptions(options);

        Assert.Same(options, result);
        Assert.Equal(converterCountBefore, result.Converters.Count);
    }

    [Fact]
    public void GetOptions_CalledRepeatedly_OnSameMutableInstance_DoesNotDuplicateConverters()
    {
        var options = new JsonSerializerOptions();

        // GetOptions returns a completed copy and leaves the caller's instance alone, so the
        // duplicate check runs on the copy that is handed back again.
        JsonSerializerOptions first = JsonSerializer.GetOptions(options);
        JsonSerializerOptions second = JsonSerializer.GetOptions(first);
        JsonSerializerOptions third = JsonSerializer.GetOptions(second);

        // Each converter TYPE in the library's default set must appear at most once, however
        // often the completed options are passed through GetOptions() again.
        var converterTypeCounts = third
            .Converters.GroupBy(c => c.GetType())
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.All(converterTypeCounts, kvp => Assert.Equal(1, kvp.Value));
    }

    [Fact]
    public void GetOptions_MutableCallerOptions_AreNotChanged_AndACompletedCopyIsReturned()
    {
        var callerOptions = new JsonSerializerOptions { WriteIndented = false };

        JsonSerializerOptions result = JsonSerializer.GetOptions(callerOptions, pretty: true);

        Assert.Empty(callerOptions.Converters);
        Assert.False(callerOptions.WriteIndented);
        Assert.NotSame(callerOptions, result);
        Assert.NotEmpty(result.Converters);
        Assert.True(result.WriteIndented);
    }

    [Fact]
    public void Deserialize_WithExplicitOptions_UsesTheSharedConverters()
    {
        // System.Text.Json has no built-in IPAddress converter, so this only works when the
        // shared IpAddressJsonConverter is added to the caller's plain options.
        var plain = new JsonSerializerOptions();

        Addressed result = JsonSerializer.Deserialize<Addressed>(
            """{"Address":"192.0.2.1"}""",
            plain
        );

        Assert.Equal(IPAddress.Parse("192.0.2.1"), result.Address);
        Assert.Empty(plain.Converters);
    }

    [Fact]
    public void Serialize_WithExplicitOptions_LeavesTheCallersOptionsUnchanged()
    {
        var plain = new JsonSerializerOptions();

        string json = JsonSerializer.Serialize(
            new Addressed { Address = IPAddress.Loopback },
            plain
        );

        Assert.Contains("127.0.0.1", json);
        Assert.Empty(plain.Converters);
        Assert.False(plain.IsReadOnly);
    }

    [Fact]
    public void Serialize_Compact_IsByteIdenticalToTheOptionsOutput_AndKeepsTextExact()
    {
        // Stored JSON (records, journal, backups, broker messages) goes through this serializer,
        // so the compact bytes must not change when the sanitising pass is gone. The expected
        // text is written out because comparing with the same options alone would prove nothing.
        var payload = new Simple { Value = "q\" c\": \"d \\ line\nnext \u00e9\u4e2d" };
        const string expected =
            "{\"Value\":\"q\\u0022 c\\u0022: \\u0022d \\\\ line\\nnext \\u00E9\\u4E2D\"}";

        string json = JsonSerializer.Serialize(payload);

        Assert.Equal(expected, json);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(
                payload,
                typeof(Simple),
                JsonSerializer.GetOptions()
            ),
            json
        );
        Assert.Equal(payload.Value, JsonSerializer.Deserialize<Simple>(json).Value);
    }

    [Fact]
    public void SerializePretty_Output_DeserializesBackToAnEqualPayload()
    {
        var payload = new Simple { Value = "a \"quoted\": value\nline" };

        string json = JsonSerializer.SerializePretty(payload);

        Assert.Contains("\n", json);
        Assert.Equal(payload.Value, JsonSerializer.Deserialize<Simple>(json).Value);
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
    public void Serialize_WithExplicitConcreteType_SerializesConcreteMembers()
    {
        // The non-generic Serialize(object payload) overload takes the type from
        // payload.GetType() (not the compile-time type), so an interface-typed variable still
        // serializes the concrete shape.
        IHasName asInterface = new Concrete { Name = "n", OnlyOnConcrete = 42 };

        string json = JsonSerializer.Serialize((object)asInterface);

        Assert.Contains("\"OnlyOnConcrete\":42", json);
    }

    [Fact]
    public void SerializeGeneric_WithInterfaceTypeArgument_SerializesOnlyInterfaceMembers()
    {
        // The generic Serialize<TSource>(TSource payload) overload passes typeof(TSource), so an
        // interface TSource serializes only the interface's properties. This mirrors
        // System.Text.Json's generic API; the test pins the behavior.
        IHasName asInterface = new Concrete { Name = "n", OnlyOnConcrete = 42 };

        string json = JsonSerializer.Serialize(asInterface);

        Assert.Contains("\"Name\":\"n\"", json);
        Assert.DoesNotContain("OnlyOnConcrete", json);
    }
}
