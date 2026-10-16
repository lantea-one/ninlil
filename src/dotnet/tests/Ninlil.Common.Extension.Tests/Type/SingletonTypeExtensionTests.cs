using System.Collections.Concurrent;
using Ninlil.Common.Extension.Tests.TestSupport;
using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Type;

/// <summary>
///     These tests cover <see cref="SingletonTypeExtension" />. Its store is process-wide static
///     state, so each test uses model types that belong to this class alone to avoid interference
///     from other tests. The concurrency tests release real threads together with a barrier and
///     count constructor runs.
/// </summary>
public class SingletonTypeExtensionTests
{
    /// <summary>
    ///     A model type used only by these tests, so other test classes running in parallel cannot
    ///     change the shared static store for it.
    /// </summary>
    private class SingletonProbeModel
    {
        public Guid InstanceId { get; } = Guid.NewGuid();
    }

    [Fact]
    public void CanSingletonReturnsTrueForConcreteClass()
    {
        Assert.True(typeof(SingletonProbeModel).CanSingleton());
    }

    [Fact]
    public void CanSingletonReturnsFalseForAbstractClass()
    {
        Assert.False(typeof(SampleAbstractModel).CanSingleton());
    }

    [Fact]
    public void CreateSingletonInstanceReturnsANewInstanceEachCall()
    {
        SingletonProbeModel first = typeof(SingletonProbeModel).CreateSingletonInstance();
        SingletonProbeModel second = typeof(SingletonProbeModel).CreateSingletonInstance();

        Assert.NotSame(first, second);
    }

    [Fact]
    public void CreateSingletonInstanceThrowsForAbstractType()
    {
        Assert.Throws<ArgumentException>(() =>
            typeof(SampleAbstractModel).CreateSingletonInstance()
        );
    }

    [Fact]
    public void GetSingletonInstanceReturnsTheSameInstanceAcrossCalls()
    {
        // Use a nested type unique to this test so another test's instance of
        // SingletonProbeModel cannot change the identity being checked.
        object first = typeof(GetSingletonProbeModel).GetSingletonInstance();
        object second = typeof(GetSingletonProbeModel).GetSingletonInstance();

        Assert.Same(first, second);
    }

    /// <summary>
    ///     A model type used only by <see cref="GetSingletonInstanceReturnsTheSameInstanceAcrossCalls" />.
    /// </summary>
    private class GetSingletonProbeModel;

    [Fact]
    public void GetSingletonInstanceReturnsANewInstanceAfterCreateSingletonInstanceReplacesIt()
    {
        SingletonProbeModel first = typeof(SingletonProbeModel).GetSingletonInstance();
        SingletonProbeModel replaced = typeof(SingletonProbeModel).CreateSingletonInstance();
        SingletonProbeModel second = typeof(SingletonProbeModel).GetSingletonInstance();

        Assert.NotSame(first, replaced);
        Assert.Same(replaced, second);
    }

    [Fact]
    public void GetSingletonInstanceGenericReturnsTypedInstance()
    {
        // Use a nested type unique to this test so another test's use of SingletonProbeModel
        // cannot change the shared store underneath it.
        SampleModel result = typeof(SampleModel).GetSingletonInstance<SampleModel>();

        Assert.NotNull(result);
    }

    /// <summary>
    ///     A type whose constructor counts its runs and sleeps, so concurrent first calls overlap
    ///     inside the constructor. Used only by the concurrent get test.
    /// </summary>
    private class SlowConstructedModel
    {
        /// <summary>
        ///     The number of constructor runs.
        /// </summary>
        public static int Constructed;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SlowConstructedModel" /> class.
        /// </summary>
        public SlowConstructedModel()
        {
            Interlocked.Increment(ref Constructed);
            Thread.Sleep(50);
        }
    }

    /// <summary>
    ///     A type whose constructor throws on its first run only, to prove a failed construction
    ///     leaves no entry behind.
    /// </summary>
    private class FailsOnceModel
    {
        /// <summary>
        ///     The number of constructor runs.
        /// </summary>
        public static int Runs;

        /// <summary>
        ///     Initializes a new instance of the <see cref="FailsOnceModel" /> class.
        /// </summary>
        public FailsOnceModel()
        {
            if (Interlocked.Increment(ref Runs) == 1)
                throw new InvalidOperationException("first construction fails");
        }
    }

    [Fact]
    public async Task GetSingletonInstance_CalledByManyThreadsAtOnce_ConstructsOneInstance()
    {
        const int threads = 8;
        using Barrier barrier = new(threads);
        ConcurrentBag<object> results = [];

        Task[] tasks = System
            .Linq.Enumerable.Range(0, threads)
            .Select(_ =>
                Task.Factory.StartNew(
                    () =>
                    {
                        barrier.SignalAndWait();
                        results.Add(typeof(SlowConstructedModel).GetSingletonInstance());
                    },
                    TaskCreationOptions.LongRunning
                )
            )
            .ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(1, SlowConstructedModel.Constructed);
        Assert.Equal(threads, results.Count);
        Assert.Single(results.Distinct());
    }

    [Fact]
    public void GetSingletonInstance_AfterAConstructorThrew_ConstructsAgain()
    {
        // Reflection wraps the constructor's exception, so only the inner type is checked.
        Exception failure = Assert.ThrowsAny<Exception>(() =>
            typeof(FailsOnceModel).GetSingletonInstance()
        );
        Assert.IsType<InvalidOperationException>(failure.InnerException ?? failure);

        object second = typeof(FailsOnceModel).GetSingletonInstance();
        object third = typeof(FailsOnceModel).GetSingletonInstance();

        Assert.NotNull(second);
        Assert.Same(second, third);
        Assert.Equal(2, FailsOnceModel.Runs);
    }
}
