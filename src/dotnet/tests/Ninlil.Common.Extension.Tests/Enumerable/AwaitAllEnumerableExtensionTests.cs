using Ninlil.Common.Extension.Enumerable;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Enumerable;

/// <summary>
///     These tests await collections of completed and failed tasks through each
///     <see cref="AwaitAllEnumerableExtension" /> method and check the results, ordering, empty
///     input, pre-cancelled tokens and exception propagation.
/// </summary>
public class AwaitAllEnumerableExtensionTests
{
    /// <summary>
    ///     This helper builds a small collection of already-completed tasks producing
    ///     the values 1 through <paramref name="count" />, in order.
    /// </summary>
    private static List<Task<int>> BuildTasks(int count) =>
        [.. System.Linq.Enumerable.Range(1, count).Select(i => Task.FromResult(i))];

    [Fact]
    public async Task AwaitAllReturnsArrayOfAllResultsInOrder()
    {
        int[] result = await BuildTasks(3).AwaitAll();

        Assert.Equal([1, 2, 3], result);
    }

    [Fact]
    public async Task AwaitAllOnEmptyCollectionReturnsEmptyArray()
    {
        int[] result = await new List<Task<int>>().AwaitAll();

        Assert.Empty(result);
    }

    [Fact]
    public async Task AwaitAllThrowsWhenCancellationAlreadyRequested()
    {
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            BuildTasks(2).AwaitAll(cts.Token)
        );
    }

    [Fact]
    public async Task AwaitAllPropagatesExceptionFromAFailedTask()
    {
        List<Task<int>> tasks =
        [
            Task.FromResult(1),
            Task.FromException<int>(new InvalidOperationException("boom")),
        ];

        await Assert.ThrowsAsync<InvalidOperationException>(() => tasks.AwaitAll());
    }

    [Fact]
    public async Task AwaitAllListReturnsListOfAllResultsInOrder()
    {
        List<int> result = await BuildTasks(3).AwaitAllList();

        Assert.Equal([1, 2, 3], result);
    }

    [Fact]
    public async Task AwaitAllListOnEmptyCollectionReturnsEmptyList()
    {
        List<int> result = await new List<Task<int>>().AwaitAllList();

        Assert.Empty(result);
    }

    [Fact]
    public async Task AwaitAllEnumerableReturnsEnumerableOfAllResults()
    {
        IEnumerable<int> result = await BuildTasks(3).AwaitAllEnumerable();

        Assert.Equal([1, 2, 3], result);
    }

    [Fact]
    public async Task AwaitAllEnumerableOnEmptyCollectionReturnsEmptyEnumerable()
    {
        IEnumerable<int> result = await new List<Task<int>>().AwaitAllEnumerable();

        Assert.Empty(result);
    }
}
