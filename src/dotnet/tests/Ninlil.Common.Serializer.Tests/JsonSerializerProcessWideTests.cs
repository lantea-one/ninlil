using System.Text.Json;
using System.Text.Json.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests;

/// <summary>
///     This collection definition keeps the tests that change process-wide <see cref="JsonSerializer" />
///     state (default options, the shared converter list) away from every other test class. xUnit runs
///     a collection with parallelization disabled after the parallel ones, alone, so no other test
///     serializes while these change the shared defaults.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class JsonSerializerProcessWideCollection
{
    /// <summary>
    ///     This constant is the collection name used by <see cref="JsonSerializerProcessWideTests" />.
    /// </summary>
    public const string Name = "JsonSerializer process-wide state";
}

/// <summary>
///     These tests cover the cached default options of <see cref="JsonSerializer" />:
///     <see cref="JsonSerializer.SerializerOptions" />, converters added through
///     <see cref="JsonSerializer.AddConverter(JsonConverter)" /> and their thread safety. Each test restores
///     the library defaults and removes the converters it added in a <c>finally</c> block, and uses
///     converter types private to this class so no other test sees them.
/// </summary>
[Collection(JsonSerializerProcessWideCollection.Name)]
public class JsonSerializerProcessWideTests
{
    private class Simple
    {
        public string Value { get; set; }
    }

    /// <summary>
    ///     This type is the payload of <see cref="LateConverter" />; only this test file uses it.
    /// </summary>
    private class Late
    {
        public int Number { get; set; }
    }

    /// <summary>
    ///     This converter writes a <see cref="Late" /> as the fixed text <c>"late"</c>. It proves that a
    ///     converter added after the first serialization reaches the next call.
    /// </summary>
    private class LateConverter : JsonConverter<Late>
    {
        public override Late Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        ) => new() { Number = 1 };

        public override void Write(
            Utf8JsonWriter writer,
            Late value,
            JsonSerializerOptions options
        ) => writer.WriteStringValue("late");
    }

    /// <summary>
    ///     This converter is a distinct closed generic type per <typeparamref name="T" />, so
    ///     <see cref="JsonSerializer.AddConverter(JsonConverter)" /> (which de-duplicates by converter
    ///     type) treats every instantiation as a new converter. The concurrency test adds many of them.
    /// </summary>
    /// <typeparam name="T">The type the dummy converter claims to convert.</typeparam>
    private class DummyConverter<T> : JsonConverter<T>
    {
        public override T Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        ) => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            throw new NotSupportedException();
    }

    [Fact]
    public void Serialize_AfterSerializerOptionsAreSet_HonoursThem()
    {
        try
        {
            JsonSerializer.SerializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };

            string json = JsonSerializer.Serialize(new Simple { Value = "x" });

            Assert.Equal("{\"value\":\"x\"}", json);
            Assert.Equal("x", JsonSerializer.Deserialize<Simple>(json).Value);
            Assert.Contains(
                "\"value\"",
                JsonSerializer.SerializePretty(new Simple { Value = "x" })
            );
        }
        finally
        {
            JsonSerializer.SerializerOptions = null;
        }

        Assert.Equal("{\"Value\":\"x\"}", JsonSerializer.Serialize(new Simple { Value = "x" }));
    }

    [Fact]
    public void SerializerOptions_SetToCallerOptions_StoresACopyAndNeverChangesTheCaller()
    {
        var callerOptions = new JsonSerializerOptions();
        try
        {
            JsonSerializer.SerializerOptions = callerOptions;
            _ = JsonSerializer.Serialize(new Simple { Value = "x" });

            Assert.NotSame(callerOptions, JsonSerializer.SerializerOptions);
            Assert.Empty(callerOptions.Converters);
            Assert.False(callerOptions.IsReadOnly);
        }
        finally
        {
            JsonSerializer.SerializerOptions = null;
        }
    }

    [Fact]
    public void SerializerOptions_Read_CarriesTheSharedConverters_AndIsTheSameInstanceEachTime()
    {
        JsonSerializerOptions first = JsonSerializer.SerializerOptions;
        JsonSerializerOptions second = JsonSerializer.SerializerOptions;

        Assert.Same(first, second);
        foreach (JsonConverter shared in JsonSerializer.Converters)
            Assert.Contains(first.Converters, c => c.GetType() == shared.GetType());
    }

    [Fact]
    public void AddConverter_AfterTheFirstSerialization_IsUsedByTheNextCall()
    {
        var converter = new LateConverter();
        try
        {
            // Serialize once so the defaults exist (and are cached) before the converter arrives.
            _ = JsonSerializer.Serialize(new Simple { Value = "x" });
            Assert.Equal("{\"Number\":3}", JsonSerializer.Serialize(new Late { Number = 3 }));

            JsonSerializer.AddConverter(converter);

            Assert.Equal("\"late\"", JsonSerializer.Serialize(new Late { Number = 3 }));
            Assert.Equal("\"late\"", JsonSerializer.SerializePretty(new Late { Number = 3 }));
            Assert.Equal(1, JsonSerializer.Deserialize<Late>("\"anything\"").Number);
        }
        finally
        {
            JsonSerializer.Converters.Remove(converter);
            JsonSerializer.SerializerOptions = null;
        }
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
    public async Task AddConverter_WhileOtherThreadsSerialize_LosesNoConverterAndThrowsNothing()
    {
        // Distinct converter types: a Dictionary of two array types of different depths gives
        // 40 x 40 different type arguments, so de-duplication by converter type never skips one.
        const int Threads = 8;
        const int Depth = 40;
        List<JsonConverter> converters = [];
        for (int a = 1; a <= Depth; a++)
        for (int b = 1; b <= Depth; b++)
        {
            Type key = ArrayOfDepth(a);
            Type value = ArrayOfDepth(b);
            Type target = typeof(Dictionary<,>).MakeGenericType(key, value);
            converters.Add(
                (JsonConverter)
                    Activator.CreateInstance(typeof(DummyConverter<>).MakeGenericType(target))
            );
        }

        var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        int adding = Threads;
        try
        {
            Task[] adders = Enumerable
                .Range(0, Threads)
                .Select(t =>
                    Task.Run(() =>
                    {
                        try
                        {
                            for (int i = t; i < converters.Count; i += Threads)
                                JsonSerializer.AddConverter(converters[i]);
                        }
                        catch (Exception exception)
                        {
                            failures.Add(exception);
                        }
                        finally
                        {
                            Interlocked.Decrement(ref adding);
                        }
                    })
                )
                .ToArray();

            // The serializers run for as long as any adder is still working, and at least a
            // short while, so adds and serializations overlap.
            DateTime floor = DateTime.UtcNow.AddMilliseconds(300);
            Task[] serializers = Enumerable
                .Range(0, Threads)
                .Select(_ =>
                    Task.Run(() =>
                    {
                        try
                        {
                            while (Volatile.Read(ref adding) > 0 || DateTime.UtcNow < floor)
                            {
                                string json = JsonSerializer.Serialize(new Simple { Value = "x" });
                                Assert.Equal("x", JsonSerializer.Deserialize<Simple>(json).Value);
                            }
                        }
                        catch (Exception exception)
                        {
                            failures.Add(exception);
                        }
                    })
                )
                .ToArray();

            await Task.WhenAll(adders.Concat(serializers));

            Assert.Empty(failures);
            foreach (JsonConverter added in converters)
                Assert.Contains(
                    JsonSerializer.Converters.ToArray(),
                    c => c.GetType() == added.GetType()
                );
            JsonSerializerOptions current = JsonSerializer.SerializerOptions;
            foreach (JsonConverter added in converters)
                Assert.Contains(current.Converters, c => c.GetType() == added.GetType());
        }
        finally
        {
            JsonSerializer.Converters.RemoveAll(c => converters.Contains(c));
            JsonSerializer.SerializerOptions = null;
        }
    }

    /// <summary>
    ///     This method builds the array type <c>int[]...[]</c> with the given number of brackets.
    /// </summary>
    /// <param name="depth">The number of nested array levels.</param>
    /// <returns>The nested array type.</returns>
    private static Type ArrayOfDepth(int depth)
    {
        Type type = typeof(int);
        for (int i = 0; i < depth; i++)
            type = type.MakeArrayType();
        return type;
    }
}
