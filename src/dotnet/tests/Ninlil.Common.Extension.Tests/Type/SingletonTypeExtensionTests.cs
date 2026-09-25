using Ninlil.Common.Extension.Tests.TestSupport;
using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Type;

/// <summary>
///     These tests cover <see cref="SingletonTypeExtension" />. Its store is a process-wide static
///     list, so each test uses model types that belong to this class alone to avoid interference
///     from other tests.
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
}
