using Ninlil.Common.Extension.Tests.TestSupport;
using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Type;

/// <summary>
///     This class tests <see cref="CreateInstanceTypeExtension" />.
/// </summary>
public class CreateInstanceTypeExtensionTests
{
    [Fact]
    public void CreateInstanceOnTypeWithParameterlessConstructorReturnsNewInstance()
    {
        SampleModel result = typeof(SampleModel).CreateInstance();

        Assert.NotNull(result);
        Assert.Null(result.Name);
    }

    [Fact]
    public void CreateInstanceOnValueTypeReturnsDefaultValue()
    {
        int result = typeof(int).CreateInstance();

        Assert.Equal(0, result);
    }

    [Fact]
    public void CreateInstanceOnTypeWithoutParameterlessConstructorReturnsNull()
    {
        object result = typeof(SampleModelWithRequiredConstructorArgument).CreateInstance();

        Assert.Null(result);
    }

    [Fact]
    public void CreateInstanceWithConstructorArgumentsUsesMatchingConstructor()
    {
        SampleModelWithRequiredConstructorArgument result =
            typeof(SampleModelWithRequiredConstructorArgument).CreateInstance(42);

        Assert.Equal(42, result.Seed);
    }

    [Fact]
    public void CreateInstanceGenericOnTypeWithParameterlessConstructorReturnsTypedInstance()
    {
        SampleModel result = typeof(SampleModel).CreateInstance<SampleModel>();

        Assert.NotNull(result);
    }

    [Fact]
    public void CreateInstanceGenericWithConstructorArgumentsReturnsTypedInstance()
    {
        SampleModelWithRequiredConstructorArgument result =
            typeof(SampleModelWithRequiredConstructorArgument).CreateInstance<SampleModelWithRequiredConstructorArgument>(
                7
            );

        Assert.Equal(7, result.Seed);
    }
}
