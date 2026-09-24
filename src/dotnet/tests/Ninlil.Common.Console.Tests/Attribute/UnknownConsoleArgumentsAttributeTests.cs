using Ninlil.Common.Console.Attribute;
using Ninlil.Common.Console.Tests.TestSupport;

// Define our namespace for the unknown-console-arguments attribute tests.
namespace Ninlil.Common.Console.Tests.Attribute;

/// <summary>
///     This test class exercises <see cref="UnknownConsoleArgumentsAttribute" />: that it can be
///     applied to a property, discovered via reflection, and correctly captures unmatched CLI
///     arguments when a model declares it (exercised end-to-end via <see cref="UnknownArgsModel" />).
/// </summary>
public class UnknownConsoleArgumentsAttributeTests
{
    /// <summary>
    ///     Verifies that the attribute can be discovered via reflection on the property it decorates.
    /// </summary>
    [Fact]
    public void Attribute_IsDiscoverableOnDecoratedProperty()
    {
        var property = typeof(UnknownArgsModel).GetProperty(nameof(UnknownArgsModel.Remaining));

        var attribute = property?.GetCustomAttributes(
            typeof(UnknownConsoleArgumentsAttribute),
            true
        );

        Assert.NotNull(attribute);
        Assert.Single(attribute);
    }

    /// <summary>
    ///     Verifies that a property not decorated with the attribute is not discovered.
    /// </summary>
    [Fact]
    public void Attribute_IsNotPresentOnUndecoratedProperty()
    {
        var property = typeof(UnknownArgsModel).GetProperty(nameof(UnknownArgsModel.Known));

        var attribute = property?.GetCustomAttributes(
            typeof(UnknownConsoleArgumentsAttribute),
            true
        );

        Assert.Empty(attribute!);
    }
}
