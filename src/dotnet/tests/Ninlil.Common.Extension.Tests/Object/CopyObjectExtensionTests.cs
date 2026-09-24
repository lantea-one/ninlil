using Ninlil.Common.Extension.Object;
using Ninlil.Common.Extension.Tests.TestSupport;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Object;

/// <summary>
///     This class tests <see cref="CopyObjectExtension" />.
/// </summary>
/// <remarks>
///     <c>CopyObjectExtension</c> used to also declare a same-type convenience overload,
///     <c>Copy&lt;TClass&gt;(this TClass source, TClass destination)</c> (and its constructor-arguments
///     sibling), but each was a real, confirmed compile-time-ambiguous overload with
///     <c>Copy&lt;TSource, TDestination&gt;(TSource, TDestination)</c> whenever <c>TSource</c> and
///     <c>TDestination</c> happened to be the same type -- the compiler could not choose between them and
///     refused to build (CS0121) unless the call site supplied explicit type arguments. Both
///     single-type-parameter overloads have been removed: ordinary generic type inference already resolves
///     the two-type-parameter overloads to the same-type case on its own, so every call shape below
///     (implicit inference and explicit <c>.Copy&lt;TSource, TDestination&gt;(...)</c> alike) now works
///     without ambiguity.
/// </remarks>
public class CopyObjectExtensionTests
{
    [Fact]
    public void CopyIntoExistingDestinationCopiesAllWritableProperties()
    {
        SampleModel source = new() { Name = "source", Number = 5 };
        SampleModel destination = new() { Name = "will-be-overwritten", Number = 0 };

        SampleModel result = source.Copy(destination);

        Assert.Same(destination, result);
        Assert.Equal("source", result.Name);
        Assert.Equal(5, result.Number);
    }

    [Fact]
    public void CopyWithConstructorArgumentsCreatesANewPopulatedInstance()
    {
        SampleModelWithRequiredConstructorArgument source = new(9);

        SampleModelWithRequiredConstructorArgument result = source.Copy<
            SampleModelWithRequiredConstructorArgument,
            SampleModelWithRequiredConstructorArgument
        >(9);

        Assert.NotSame(source, result);
        Assert.Equal(9, result.Seed);
    }

    /// <summary>
    ///     <c>Copy</c> now reflects over <c>source.GetType()</c> -- the value's actual runtime type --
    ///     rather than the compile-time <c>TSource</c> type parameter, so a source reference declared as a
    ///     looser type than its real value (here, <see cref="object" /> holding a <see cref="SampleModel" />)
    ///     still copies its real property values.
    /// </summary>
    [Fact]
    public void CopyFromASourceDeclaredAsALooserTypeStillCopiesItsRuntimeTypesProperties()
    {
        object source = new SampleModel { Name = "cross-type", Number = 3 };
        SampleModel destination = new();

        SampleModel result = source.Copy(destination);

        Assert.Equal("cross-type", result.Name);
        Assert.Equal(3, result.Number);
    }

    [Fact]
    public void CopySingleTypeWithConstructorArgumentsCreatesNewInstance()
    {
        SampleModel source = new() { Name = "two", Number = 2 };

        SampleModel result = source.Copy<SampleModel, SampleModel>();

        Assert.NotSame(source, result);
        Assert.Equal("two", result.Name);
        Assert.Equal(2, result.Number);
    }

    [Fact]
    public void CopyDoesNotThrowWhenDestinationHasAReadOnlyProperty()
    {
        SampleModel source = new() { Name = "read-only-safe" };
        SampleModel destination = new();

        // FixedValue is read-only (get-only), so Copy skips it via property.CanWrite -- this
        // asserts the writable properties still copy correctly around it, without throwing.
        SampleModel result = source.Copy<SampleModel, SampleModel>(destination);

        Assert.Equal("read-only-safe", result.Name);
        Assert.Equal("fixed", result.FixedValue);
    }
}
