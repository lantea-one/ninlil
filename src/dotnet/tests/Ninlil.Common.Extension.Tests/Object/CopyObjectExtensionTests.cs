using Ninlil.Common.Extension.Object;
using Ninlil.Common.Extension.Tests.TestSupport;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Object;

/// <summary>
///     These tests copy <c>SampleModel</c> instances with <see cref="CopyObjectExtension" />, onto an
///     existing object and into a new one, including a source declared as <see cref="object" /> and
///     a read-only property.
/// </summary>
/// <remarks>
///     The calls below use both inferred and explicit <c>Copy&lt;TSource, TDestination&gt;</c>
///     type arguments. They compile only because there is no single-type-parameter
///     <c>Copy&lt;TClass&gt;</c> overload, which would be ambiguous (CS0121) when both types are
///     the same.
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
    ///     <c>Copy</c> reflects over <c>source.GetType()</c>, the runtime type, not <c>TSource</c>, so a
    ///     source declared as <see cref="object" /> holding a <see cref="SampleModel" /> still copies its
    ///     property values.
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

        // FixedValue is get-only, so Copy skips it (property.CanWrite). The writable properties
        // must still copy without throwing.
        SampleModel result = source.Copy<SampleModel, SampleModel>(destination);

        Assert.Equal("read-only-safe", result.Name);
        Assert.Equal("fixed", result.FixedValue);
    }
}
