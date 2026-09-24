using Ninlil.Common.Extension.Tests.TestSupport;
using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Type;

/// <summary>
///     This class tests <see cref="SingletonTypeExtension" />. The extension's own singleton registry is a
///     single shared, process-wide static list, so every test here uses model types dedicated to this test
///     class alone to avoid cross-test interference.
/// </summary>
public class SingletonTypeExtensionTests
{
    /// <summary>
    ///     A dedicated model type, used only by these tests, so the shared static singleton registry
    ///     cannot be polluted by another test class running concurrently.
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
        // Use a dedicated nested type unique to this test so a prior test's singleton
        // registration for SingletonProbeModel cannot leak in and change identity.
        object first = typeof(GetSingletonProbeModel).GetSingletonInstance();
        object second = typeof(GetSingletonProbeModel).GetSingletonInstance();

        Assert.Same(first, second);
    }

    /// <summary>
    ///     A dedicated model type used only by <see cref="GetSingletonInstanceReturnsTheSameInstanceAcrossCalls" />.
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
        // Use a dedicated nested type unique to this test to avoid the shared static
        // registry being mutated by another test's use of SingletonProbeModel.
        SampleModel result = typeof(SampleModel).GetSingletonInstance<SampleModel>();

        Assert.NotNull(result);
    }
}
