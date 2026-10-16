using System.Collections.Concurrent;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests;

/// <summary>
///     This collection definition keeps the tests that change the process-wide
///     <see cref="XmlSerializer" /> namespace set away from every other test class. xUnit runs a
///     collection with parallelization disabled after the parallel ones, alone, so no other test
///     serializes XML while these add namespaces.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class XmlSerializerProcessWideCollection
{
    /// <summary>
    ///     This constant is the collection name used by <see cref="XmlSerializerNamespaceTests" />.
    /// </summary>
    public const string Name = "XmlSerializer process-wide namespaces";
}

/// <summary>
///     These tests cover <see cref="XmlSerializer.AddNamespace" /> and
///     <see cref="XmlSerializer.ClearNamespaces" />: the namespace shows up in the output, clearing
///     removes it and changing the set while other threads serialize is safe. Each test clears the
///     namespaces in a <c>finally</c> block so the default is restored.
/// </summary>
[Collection(XmlSerializerProcessWideCollection.Name)]
public class XmlSerializerNamespaceTests
{
    /// <summary>
    ///     This type is the payload the tests serialize; only this file uses it.
    /// </summary>
    public class Simple
    {
        /// <summary>
        ///     This property carries the value written into the document.
        /// </summary>
        public string Value { get; set; }
    }

    [Fact]
    public void AddNamespace_And_ClearNamespaces_DoNotThrow()
    {
        try
        {
            XmlSerializer.AddNamespace("test", "http://example.com/test");
            string xml = XmlSerializer.Serialize(new Simple { Value = "x" });
            Assert.Contains("http://example.com/test", xml);
        }
        finally
        {
            XmlSerializer.ClearNamespaces();
        }

        string cleared = XmlSerializer.Serialize(new Simple { Value = "x" });
        Assert.DoesNotContain("http://example.com/test", cleared);
    }

    // Fails today (probabilistically): serializing enumerates the shared set while it is changed.
    [Fact]
    public void AddNamespace_WhileOtherThreadsSerialize_NeverThrows()
    {
        const int threadsPerRole = 8;
        var failures = new ConcurrentBag<Exception>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var threads = new List<Thread>();

        try
        {
            for (int t = 0; t < threadsPerRole; t++)
            {
                int id = t;

                // Each adder cycles over a few dozen prefixes so the set stays small.
                threads.Add(
                    new Thread(() =>
                    {
                        try
                        {
                            for (int i = 0; !stop.IsCancellationRequested; i++)
                                XmlSerializer.AddNamespace(
                                    $"t{id}n{i % 40}",
                                    $"http://example.com/{id}/{i % 40}"
                                );
                        }
                        catch (Exception exception)
                        {
                            failures.Add(exception);
                        }
                    })
                );
                threads.Add(
                    new Thread(() =>
                    {
                        try
                        {
                            while (!stop.IsCancellationRequested)
                                XmlSerializer.Serialize(new Simple { Value = "x" });
                        }
                        catch (Exception exception)
                        {
                            failures.Add(exception);
                        }
                    })
                );
            }

            threads.ForEach(thread => thread.Start());
            threads.ForEach(thread => thread.Join());
        }
        finally
        {
            XmlSerializer.ClearNamespaces();
        }

        Assert.True(
            failures.IsEmpty,
            string.Join(Environment.NewLine, failures.Select(f => f.ToString()))
        );
        Assert.DoesNotContain(
            "http://example.com/",
            XmlSerializer.Serialize(new Simple { Value = "x" })
        );
    }
}
