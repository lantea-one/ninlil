using YamlDotNet.Core;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests;

/// <summary>
///     These tests cover the thread safety of the shared converter list of
///     <see cref="YamlSerializer" />: <see cref="YamlSerializer.AddConverter(IYamlTypeConverter)" />
///     runs on several threads while others build serializers and deserializers from the list. The
///     list is private and process-wide, so the converters a test adds stay for the rest of the run;
///     they accept no type, so they never change a result, and the class runs in the
///     non-parallel <see cref="JsonSerializerProcessWideCollection" /> so the extra entries never
///     slow the parallel tests.
/// </summary>
[Collection(JsonSerializerProcessWideCollection.Name)]
public class YamlSerializerProcessWideTests
{
    private class Simple
    {
        public string Value { get; set; }
    }

    /// <summary>
    ///     This converter is a distinct closed generic type per <typeparamref name="T" />, so
    ///     <see cref="YamlSerializer.AddConverter(IYamlTypeConverter)" /> (which de-duplicates by
    ///     converter type) treats every instantiation as a new converter. It accepts no type.
    /// </summary>
    /// <typeparam name="T">The type argument that makes each instantiation distinct.</typeparam>
    private class DummyConverter<T> : IYamlTypeConverter
    {
        public bool Accepts(Type type) => false;

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) =>
            throw new NotSupportedException();

        public void WriteYaml(
            IEmitter emitter,
            object value,
            Type type,
            ObjectSerializer serializer
        ) => throw new NotSupportedException();
    }

    [Fact]
    public async Task AddConverter_WhileOtherThreadsSerialize_ThrowsNothing()
    {
        // Distinct converter types: a Dictionary of two array types of different depths gives
        // 20 x 20 different type arguments, so de-duplication by converter type never skips one.
        const int Threads = 8;
        const int Depth = 20;
        List<IYamlTypeConverter> converters = [];
        for (int a = 1; a <= Depth; a++)
        for (int b = 1; b <= Depth; b++)
        {
            Type target = typeof(Dictionary<,>).MakeGenericType(ArrayOfDepth(a), ArrayOfDepth(b));
            converters.Add(
                (IYamlTypeConverter)
                    Activator.CreateInstance(typeof(DummyConverter<>).MakeGenericType(target))
            );
        }

        var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        int adding = Threads;
        Task[] adders = Enumerable
            .Range(0, Threads)
            .Select(t =>
                Task.Run(() =>
                {
                    try
                    {
                        for (int i = t; i < converters.Count; i += Threads)
                            YamlSerializer.AddConverter(converters[i]);
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

        // The serializers run for as long as any adder is still working, and at least a short
        // while, so adds and builds overlap.
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
                            string yaml = YamlSerializer.Serialize(new Simple { Value = "x" });
                            Assert.Equal("x", YamlSerializer.Deserialize<Simple>(yaml).Value);
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
